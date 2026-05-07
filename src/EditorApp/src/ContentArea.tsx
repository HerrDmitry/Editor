// ContentArea.tsx — compiled by tsc to wwwroot/js/ContentArea.js
// No module imports — React is a global from the UMD script.

//
// Sliding-window virtual scroll:
//   - Container holds up to WINDOW_SIZE rendered lines (real DOM, real heights).
//   - Viewport clips with overflow:auto → native pixel-smooth scroll.
//   - When scroll approaches edge, request more lines from backend.
//   - App merges new lines into buffer (no trim).
//   - useLayoutEffect trims buffer + adjusts scrollTop before paint → no jump.
//

/** Max logical lines to keep in the sliding window. */
const WINDOW_SIZE = 400;

/** When scroll gets within this many pixels of container edge, fetch more. */
const EDGE_THRESHOLD = 600;

/** Lines to request from backend per fetch. */
const FETCH_SIZE = 200;

/** Fixed line height for line-number column. */
const LINE_HEIGHT = 20;

/** Viewport size for scrollbar thumb sizing (logical lines). */
const SCROLLBAR_VIEWPORT_SIZE = 50;

// --- Horizontal virtualization constants ---

/** Lines with more chars than this are "large" and use horizontal virtualization. */
const LARGE_LINE_THRESHOLD = 65_536;

/** Number of characters visible in the horizontal viewport. */
const H_VIEWPORT_CHARS = 200;

/** Column window buffer: chars loaded ahead/behind the visible viewport. */
const H_WINDOW_CHARS = 600;

/** Max total characters stored across all chunk cache entries (~10MB at 2 bytes/char). */
const MAX_CHUNK_CACHE_CHARS = 5_000_000;

/** Timeout for copy operations that require chunk loading (ms). */
const COPY_TIMEOUT_MS = 10_000;

interface FileMeta {
  totalLines: number;
}

interface ErrorInfo {
  errorCode: string;
  message: string;
  details?: string;
}

interface ContentAreaProps {
  fileMeta: FileMeta | null;
  lines: string[] | null;
  linesStartLine: number;
  isLoading: boolean;
  error: ErrorInfo | null;
  wrapLines: boolean;
  onRequestLines: (startLine: number, lineCount: number) => void;
  onJumpToLine: (startLine: number, lineCount: number) => void;
  onTrimBuffer: (newStart: number, newLines: string[]) => void;
}

/** Entry in the per-line chunk cache. */
interface ChunkCacheEntry {
  startCol: number;
  text: string;
  /** Insertion order for LRU eviction. */
  lruOrder: number;
}

/** Active search match highlight info for a large line. */
interface SearchMatchInfo {
  lineNumber: number;
  matchColumn: number;
  matchLength: number;
}

function ContentArea({ fileMeta, lines, linesStartLine, isLoading, error, wrapLines, onRequestLines, onJumpToLine, onTrimBuffer }: ContentAreaProps) {
  const viewportRef = React.useRef<HTMLDivElement>(null);
  const containerRef = React.useRef<HTMLDivElement>(null);
  const pendingRequestRef = React.useRef(false);
  const isTrimming = React.useRef(false);
  const isJumpingRef = React.useRef(false);
  const jumpTargetLineRef = React.useRef(0);
  const dragDebounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  // Scrollbar position = first visible logical line number
  const [scrollbarPosition, setScrollbarPosition] = React.useState(0);

  // --- Horizontal virtualization state ---

  /** Current horizontal scroll column offset. */
  const [hScrollCol, setHScrollCol] = React.useState(0);

  /** Per-line chunk cache: Map<lineNumber, ChunkCacheEntry>. */
  const chunkCacheRef = React.useRef<Map<number, ChunkCacheEntry>>(new Map());

  /** LRU counter — incremented on each cache insert/access. */
  const lruCounterRef = React.useRef(0);

  /** Per-line character lengths from LinesResponse. null = all lines normal. */
  const [lineLengths, setLineLengths] = React.useState<number[] | null>(null);

  /** Force re-render counter (used when chunk cache updates). */
  const [chunkVersion, setChunkVersion] = React.useState(0);

  // --- Task 10.1: Copy loading state ---
  const [copyLoading, setCopyLoading] = React.useState(false);
  const [copyError, setCopyError] = React.useState<string | null>(null);

  // --- Task 11.2: Search match highlight state ---
  const [searchMatch, setSearchMatch] = React.useState<SearchMatchInfo | null>(null);

  /** Pending copy chunk resolvers: Map<lineNumber, resolve function>. */
  const pendingCopyChunksRef = React.useRef<Map<number, (entry: ChunkCacheEntry) => void>>(new Map());

  // Refs for fresh values in callbacks
  const bufferRef = React.useRef({ start: linesStartLine, count: lines ? lines.length : 0 });
  bufferRef.current = { start: linesStartLine, count: lines ? lines.length : 0 };
  const fileMetaRef = React.useRef(fileMeta);
  fileMetaRef.current = fileMeta;
  const hScrollColRef = React.useRef(hScrollCol);
  hScrollColRef.current = hScrollCol;
  const lineLengthsRef = React.useRef(lineLengths);
  lineLengthsRef.current = lineLengths;

  // Track previous buffer to detect prepend/append
  const prevStartRef = React.useRef(linesStartLine);
  const prevCountRef = React.useRef(lines ? lines.length : 0);

  // --- Task 8.5: Wire lineLengths from LinesResponse ---
  // Register for LinesResponse via interopService to capture lineLengths.
  React.useEffect(() => {
    const interop = (window as any).interopService as any;
    if (!interop || typeof interop.onLinesResponse !== 'function') return;

    function handleLinesResponse(data: { startLine: number; lines: string[]; totalLines: number; lineLengths?: number[] }) {
      if (data.lineLengths != null) {
        setLineLengths(data.lineLengths);
      } else {
        // All-normal response — clear large-line state
        setLineLengths(null);
      }
    }

    interop.onLinesResponse(handleLinesResponse);
    // Note: InteropService accumulates callbacks; no unregister needed for this pattern.
  }, []);

  // --- Task 8.3: Register for chunk responses ---
  React.useEffect(() => {
    const interop = (window as any).interopService as any;
    if (!interop || typeof interop.onLineChunkResponse !== 'function') return;

    function handleChunkResponse(data: { lineNumber: number; startColumn: number; text: string; totalLineChars: number; hasMore: boolean }) {
      const cache = chunkCacheRef.current;
      const lruOrder = ++lruCounterRef.current;
      const entry: ChunkCacheEntry = { startCol: data.startColumn, text: data.text, lruOrder };
      cache.set(data.lineNumber, entry);

      // Resolve pending copy chunk if waiting
      const resolver = pendingCopyChunksRef.current.get(data.lineNumber);
      if (resolver) {
        resolver(entry);
        pendingCopyChunksRef.current.delete(data.lineNumber);
      }

      // Evict if total chars exceed MAX_CHUNK_CACHE_CHARS
      let totalChars = 0;
      for (const entry of cache.values()) {
        totalChars += entry.text.length;
      }
      if (totalChars > MAX_CHUNK_CACHE_CHARS) {
        // Collect entries sorted by LRU order (oldest first)
        const sorted = Array.from(cache.entries()).sort((a, b) => a[1].lruOrder - b[1].lruOrder);
        for (const [lineNum, entry] of sorted) {
          if (totalChars <= MAX_CHUNK_CACHE_CHARS) break;
          totalChars -= entry.text.length;
          cache.delete(lineNum);
        }
      }

      // Trigger re-render so newly cached chunk is displayed
      setChunkVersion(v => v + 1);
    }

    interop.onLineChunkResponse(handleChunkResponse);
  }, []);

  // --- Task 8.3: Vertical-scroll cleanup — evict cache entries outside buffer ---
  React.useEffect(() => {
    const cache = chunkCacheRef.current;
    const bufEnd = linesStartLine + (lines ? lines.length : 0);
    for (const lineNum of Array.from(cache.keys())) {
      if (lineNum < linesStartLine || lineNum >= bufEnd) {
        cache.delete(lineNum);
      }
    }
  }, [linesStartLine, lines]);

  // useLayoutEffect: runs after DOM update, before paint.
  // Handles scroll adjustment for prepended lines and trimming.
  React.useLayoutEffect(() => {
    if (!lines || !viewportRef.current || !containerRef.current) return;
    if (isTrimming.current) {
      isTrimming.current = false;
      return; // This render was caused by our own trim — skip
    }

    // Jump from scrollbar drag — scroll to target line within the new buffer
    if (isJumpingRef.current) {
      isJumpingRef.current = false;
      const targetLine = jumpTargetLineRef.current;
      const container = containerRef.current;
      const viewport = viewportRef.current;

      // Find the target line's DOM element and scroll to it
      const lineIndex = targetLine - linesStartLine;
      const lineElements = container.querySelectorAll('.line-container');
      if (lineIndex >= 0 && lineIndex < lineElements.length) {
        let targetOffset = 0;
        for (let i = 0; i < lineIndex; i++) {
          targetOffset += (lineElements[i] as HTMLElement).offsetHeight;
        }
        viewport.scrollTop = targetOffset;
      } else {
        viewport.scrollTop = 0;
      }

      prevStartRef.current = linesStartLine;
      prevCountRef.current = lines.length;
      return;
    }

    const viewport = viewportRef.current;
    const container = containerRef.current;
    const prevStart = prevStartRef.current;
    const newStart = linesStartLine;

    // Lines were prepended (scroll up) — adjust scrollTop by added height
    if (newStart < prevStart && prevStart > 0) {
      const prependedCount = prevStart - newStart;
      const lineElements = container.querySelectorAll('.line-container');
      let addedHeight = 0;
      for (let i = 0; i < prependedCount && i < lineElements.length; i++) {
        addedHeight += (lineElements[i] as HTMLElement).offsetHeight;
      }
      viewport.scrollTop += addedHeight;
    }

    // Trim if buffer exceeds WINDOW_SIZE
    if (lines.length > WINDOW_SIZE) {
      const lineElements = container.querySelectorAll('.line-container');

      // Determine scroll direction from which end to trim
      const scrollTop = viewport.scrollTop;
      const viewportHeight = viewport.clientHeight;
      const containerHeight = container.scrollHeight;
      const distToBottom = containerHeight - scrollTop - viewportHeight;

      if (distToBottom > scrollTop) {
        // Closer to top → trim from bottom (user scrolled up, excess at bottom)
        const newLines = lines.slice(0, WINDOW_SIZE);
        isTrimming.current = true;
        onTrimBuffer(linesStartLine, newLines);
      } else {
        // Closer to bottom → trim from top
        const trimCount = lines.length - WINDOW_SIZE;
        // Measure height of lines being removed from top
        let removedHeight = 0;
        for (let i = 0; i < trimCount && i < lineElements.length; i++) {
          removedHeight += (lineElements[i] as HTMLElement).offsetHeight;
        }
        const newLines = lines.slice(trimCount);
        const newStartLine = linesStartLine + trimCount;
        // Adjust scroll BEFORE React re-renders (we're in useLayoutEffect)
        viewport.scrollTop -= removedHeight;
        isTrimming.current = true;
        onTrimBuffer(newStartLine, newLines);
      }
    }

    prevStartRef.current = linesStartLine;
    prevCountRef.current = lines.length;
  }, [linesStartLine, lines, onTrimBuffer]);

  // Scroll handler: detect edge proximity, request more lines, update scrollbar
  const handleScroll = React.useCallback(() => {
    const viewport = viewportRef.current;
    const container = containerRef.current;
    const meta = fileMetaRef.current;
    if (!viewport || !container || !meta) return;

    const scrollTop = viewport.scrollTop;
    const viewportHeight = viewport.clientHeight;
    const containerHeight = container.scrollHeight;
    const buf = bufferRef.current;

    // Compute first visible logical line by finding which line-container
    // is at the current scrollTop position.
    const lineElements = container.querySelectorAll('.line-container');
    let accHeight = 0;
    let firstVisibleLine = buf.start;
    for (let i = 0; i < lineElements.length; i++) {
      const h = (lineElements[i] as HTMLElement).offsetHeight;
      if (accHeight + h > scrollTop) {
        firstVisibleLine = buf.start + i;
        break;
      }
      accHeight += h;
    }
    setScrollbarPosition(firstVisibleLine);

    // Near bottom edge
    const distToBottom = containerHeight - scrollTop - viewportHeight;
    if (distToBottom < EDGE_THRESHOLD) {
      const bufferEnd = buf.start + buf.count;
      if (bufferEnd < meta.totalLines && !pendingRequestRef.current) {
        pendingRequestRef.current = true;
        const startLine = Math.max(0, bufferEnd - 20);
        const count = Math.min(FETCH_SIZE, meta.totalLines - startLine);
        onRequestLines(startLine, count);
      }
    }

    // Near top edge
    if (scrollTop < EDGE_THRESHOLD) {
      if (buf.start > 0 && !pendingRequestRef.current) {
        pendingRequestRef.current = true;
        const startLine = Math.max(0, buf.start - FETCH_SIZE + 20);
        const count = Math.min(FETCH_SIZE, buf.start - startLine + 20);
        onRequestLines(startLine, count);
      }
    }
  }, [onRequestLines]);

  // Scrollbar thumb drag → jump to line (debounced).
  // Only fires the backend request after drag settles (150ms).
  // Scrollbar thumb moves immediately (CustomScrollbar handles that internally).
  const handleScrollbarDrag = React.useCallback((pos: number) => {
    const meta = fileMetaRef.current;
    if (!meta) return;
    const targetLine = Math.max(0, Math.min(Math.round(pos), meta.totalLines - 1));

    // Update scrollbar position immediately for visual feedback
    setScrollbarPosition(targetLine);

    // If target is already in the buffer, scroll to it locally — no backend request
    const buf = bufferRef.current;
    if (buf.count > 0 && targetLine >= buf.start && targetLine < buf.start + buf.count) {
      const container = containerRef.current;
      const viewport = viewportRef.current;
      if (container && viewport) {
        const lineIndex = targetLine - buf.start;
        const lineElements = container.querySelectorAll('.line-container');
        if (lineIndex >= 0 && lineIndex < lineElements.length) {
          let targetOffset = 0;
          for (let i = 0; i < lineIndex; i++) {
            targetOffset += (lineElements[i] as HTMLElement).offsetHeight;
          }
          viewport.scrollTop = targetOffset;
        }
      }
      return;
    }

    // Target outside buffer — debounce backend request
    if (dragDebounceRef.current) {
      clearTimeout(dragDebounceRef.current);
    }

    dragDebounceRef.current = setTimeout(() => {
      const halfWindow = Math.floor(FETCH_SIZE / 2);
      const startLine = Math.max(0, targetLine - halfWindow);
      const count = Math.min(FETCH_SIZE, meta.totalLines - startLine);

      jumpTargetLineRef.current = targetLine;
      isJumpingRef.current = true;
      pendingRequestRef.current = true;
      onJumpToLine(startLine, count);
    }, 150);
  }, [onJumpToLine]);

  // Clear pending flag when new data arrives.
  React.useEffect(() => {
    pendingRequestRef.current = false;
  }, [linesStartLine, lines]);

  // --- Task 8.4: Horizontal scrollbar drag handler ---
  const handleHScrollChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const newCol = Number(e.target.value);
    setHScrollCol(newCol);

    // Trigger chunk requests for large lines intersecting the new viewport
    const currentLines = lines;
    const currentLineLengths = lineLengthsRef.current;
    if (!currentLines || !currentLineLengths) return;

    const interop = (window as any).interopService as any;
    if (!interop || typeof interop.sendRequestLineChunk !== 'function') return;

    const cache = chunkCacheRef.current;
    for (let i = 0; i < currentLines.length; i++) {
      const lineLength = currentLineLengths[i] ?? currentLines[i].length;
      if (lineLength <= LARGE_LINE_THRESHOLD) continue;
      if (newCol >= lineLength) continue; // line ends before viewport

      const lineNumber = linesStartLine + i;
      const cached = cache.get(lineNumber);
      const visibleEnd = Math.min(newCol + H_VIEWPORT_CHARS, lineLength);

      // Check if cache covers the new viewport
      if (cached && cached.startCol <= newCol && cached.startCol + cached.text.length >= visibleEnd) {
        continue; // cache hit
      }

      // Cache miss — request chunk centered on new viewport
      const chunkStart = Math.max(0, newCol - Math.floor((H_WINDOW_CHARS - H_VIEWPORT_CHARS) / 2));
      interop.sendRequestLineChunk(lineNumber, chunkStart, H_WINDOW_CHARS);
    }
  }, [lines, linesStartLine]);

  // --- Task 10.1: Selection-aware chunk loading for copy ---
  const handleCopy = React.useCallback((e: Event) => {
    const sel = window.getSelection();
    if (!sel || sel.isCollapsed || !lines) return;

    const currentLineLengths = lineLengthsRef.current;
    if (!currentLineLengths) return; // All normal lines → default copy works fine

    const buf = bufferRef.current;
    const cache = chunkCacheRef.current;

    // Determine which lines are in the selection
    const selRange = sel.getRangeAt(0);
    const container = containerRef.current;
    if (!container) return;

    // Find which line elements intersect the selection
    const lineElements = container.querySelectorAll('.line-container');
    interface SelectedLineInfo {
      lineNumber: number;
      lineIndex: number;
      lineLength: number;
      isLarge: boolean;
      needsChunk: boolean;
    }
    const selectedLines: SelectedLineInfo[] = [];

    for (let i = 0; i < lineElements.length; i++) {
      const lineEl = lineElements[i] as HTMLElement;
      if (!selRange.intersectsNode(lineEl)) continue;

      const lineNumber = buf.start + i;
      const lineLength = currentLineLengths[i] ?? (lines[i] ? lines[i].length : 0);
      const isLarge = lineLength > LARGE_LINE_THRESHOLD;

      let needsChunk = false;
      if (isLarge) {
        // For large lines, check if full line content is in cache
        const cached = cache.get(lineNumber);
        if (!cached || cached.startCol > 0 || (cached.startCol + cached.text.length) < lineLength) {
          needsChunk = true;
        }
      }

      selectedLines.push({ lineNumber, lineIndex: i, lineLength, isLarge, needsChunk });
    }

    // If no large lines need chunks, let default copy proceed
    const linesNeedingChunks = selectedLines.filter(l => l.needsChunk);
    if (linesNeedingChunks.length === 0) return;

    // Prevent default — we'll handle clipboard write manually
    e.preventDefault();
    setCopyLoading(true);
    setCopyError(null);

    const interop = (window as any).interopService as any;
    if (!interop || typeof interop.sendRequestLineChunk !== 'function') {
      setCopyLoading(false);
      setCopyError('Copy failed: interop not available');
      setTimeout(() => setCopyError(null), 3000);
      return;
    }

    // Request full line content for each large line that needs it
    const chunkPromises: Promise<{ lineNumber: number; entry: ChunkCacheEntry }>[] = [];

    for (const lineInfo of linesNeedingChunks) {
      const promise = new Promise<{ lineNumber: number; entry: ChunkCacheEntry }>((resolve) => {
        pendingCopyChunksRef.current.set(lineInfo.lineNumber, (entry) => {
          resolve({ lineNumber: lineInfo.lineNumber, entry });
        });
      });
      chunkPromises.push(promise);

      // Request full line (start at 0, request full length)
      interop.sendRequestLineChunk(lineInfo.lineNumber, 0, lineInfo.lineLength);
    }

    // Wait for all chunks with timeout
    const timeoutPromise = new Promise<'timeout'>((resolve) => {
      setTimeout(() => resolve('timeout'), COPY_TIMEOUT_MS);
    });

    Promise.race([
      Promise.all(chunkPromises),
      timeoutPromise,
    ]).then((result) => {
      if (result === 'timeout') {
        // Clean up pending resolvers
        for (const lineInfo of linesNeedingChunks) {
          pendingCopyChunksRef.current.delete(lineInfo.lineNumber);
        }
        setCopyLoading(false);
        setCopyError('Copy timed out — selection too large or backend unresponsive');
        setTimeout(() => setCopyError(null), 4000);
        return;
      }

      // Assemble full selection text from all selected lines
      const textParts: string[] = [];
      for (const lineInfo of selectedLines) {
        if (lineInfo.isLarge) {
          const cached = cache.get(lineInfo.lineNumber);
          if (cached) {
            textParts.push(cached.text);
          } else {
            // Fallback: use whatever is in the buffer
            textParts.push(lines[lineInfo.lineIndex] || '');
          }
        } else {
          textParts.push(lines[lineInfo.lineIndex] || '');
        }
      }

      const fullText = textParts.join('\n');

      // Write to clipboard
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(fullText).then(() => {
          setCopyLoading(false);
        }).catch(() => {
          setCopyLoading(false);
          setCopyError('Copy failed: clipboard write error');
          setTimeout(() => setCopyError(null), 3000);
        });
      } else {
        setCopyLoading(false);
        setCopyError('Copy failed: clipboard API not available');
        setTimeout(() => setCopyError(null), 3000);
      }
    });
  }, [lines]);

  // Register copy event listener on viewport element
  React.useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport) return;
    viewport.addEventListener('copy', handleCopy);
    return () => {
      viewport.removeEventListener('copy', handleCopy);
      pendingCopyChunksRef.current.clear();
    };
  }, [handleCopy]);

  // --- Task 11.2: Scroll to search match in large line ---
  const scrollToSearchMatch = React.useCallback((lineNumber: number, matchColumn: number, matchLength: number) => {
    // Set hScrollCol to center match in viewport
    const targetCol = Math.max(0, matchColumn - Math.floor(H_VIEWPORT_CHARS / 2));
    setHScrollCol(targetCol);

    // Store match info for highlighting
    setSearchMatch({ lineNumber, matchColumn, matchLength });

    // Check if chunk covering match is cached
    const cache = chunkCacheRef.current;
    const cached = cache.get(lineNumber);
    const matchEnd = matchColumn + matchLength;

    if (cached && cached.startCol <= targetCol && cached.startCol + cached.text.length >= Math.min(targetCol + H_VIEWPORT_CHARS, matchEnd)) {
      // Cache hit — chunk already covers the match area
      return;
    }

    // Cache miss — request chunk centered on match
    const interop = (window as any).interopService as any;
    if (!interop || typeof interop.sendRequestLineChunk !== 'function') return;
    const chunkStart = Math.max(0, targetCol - Math.floor((H_WINDOW_CHARS - H_VIEWPORT_CHARS) / 2));
    interop.sendRequestLineChunk(lineNumber, chunkStart, H_WINDOW_CHARS);
  }, []);

  // Expose scrollToSearchMatch on window for search system
  React.useEffect(() => {
    (window as any).scrollToSearchMatch = scrollToSearchMatch;
    return () => { delete (window as any).scrollToSearchMatch; };
  }, [scrollToSearchMatch]);

  // --- Task 8.2: Helper to request a chunk for a large line ---
  function requestChunk(lineNumber: number, visibleStart: number): void {
    const interop = (window as any).interopService as any;
    if (!interop || typeof interop.sendRequestLineChunk !== 'function') return;
    const chunkStart = Math.max(0, visibleStart - Math.floor((H_WINDOW_CHARS - H_VIEWPORT_CHARS) / 2));
    interop.sendRequestLineChunk(lineNumber, chunkStart, H_WINDOW_CHARS);
  }

  // --- Task 8.2: Render a single line (large or normal) ---
  function renderLineContent(line: string, lineIndex: number, lineNumber: number): React.ReactNode {
    const lineLength = (lineLengths && lineLengths[lineIndex] != null)
      ? lineLengths[lineIndex]
      : line.length;

    const isLarge = lineLength > LARGE_LINE_THRESHOLD;

    if (!isLarge) {
      // Normal line — render fully
      return (
        <pre
          className="content-line"
          style={{
            whiteSpace: wrapLines ? 'pre-wrap' : 'pre',
            margin: 0, flex: 1, minWidth: 0,
            wordBreak: wrapLines ? 'break-all' : 'normal',
          }}
        >{line}</pre>
      );
    }

    if (wrapLines) {
      // Large line + wrap mode: show truncated content with indicator.
      // Full wrap of a multi-MB line would freeze the browser.
      const truncatedChars = line.length;
      const totalChars = lineLength;
      return (
        <pre
          className="content-line content-line--large-wrapped"
          style={{
            whiteSpace: 'pre-wrap',
            margin: 0, flex: 1, minWidth: 0,
            wordBreak: 'break-all',
          }}
        >{line}<span className="content-line__truncation-indicator" style={{ opacity: 0.5, fontStyle: 'italic' }}>{` … [${truncatedChars.toLocaleString()} / ${totalChars.toLocaleString()} chars shown]`}</span></pre>
      );
    }

    // Large line — horizontal virtualization
    const visibleStart = hScrollCol;
    const visibleEnd = Math.min(hScrollCol + H_VIEWPORT_CHARS, lineLength);

    // Normal (short) line that ends before the horizontal viewport
    if (lineLength <= hScrollCol) {
      return (
        <pre
          className="content-line content-line--large"
          style={{ whiteSpace: 'pre', margin: 0, flex: 1, minWidth: 0 }}
        >{' '.repeat(H_VIEWPORT_CHARS)}</pre>
      );
    }

    const cached = chunkCacheRef.current.get(lineNumber);
    let visibleText: string;

    if (cached && cached.startCol <= visibleStart && cached.startCol + cached.text.length >= visibleEnd) {
      // Cache hit — extract visible portion
      const offset = visibleStart - cached.startCol;
      visibleText = cached.text.slice(offset, offset + (visibleEnd - visibleStart));
      // Update LRU order on access
      cached.lruOrder = ++lruCounterRef.current;
    } else {
      // Cache miss — show placeholder spaces, request chunk
      visibleText = ' '.repeat(visibleEnd - visibleStart);
      requestChunk(lineNumber, visibleStart);
    }

    // --- Task 11.2: Highlight search match if on this line ---
    if (searchMatch && searchMatch.lineNumber === lineNumber && visibleText !== ' '.repeat(visibleText.length)) {
      const matchStart = searchMatch.matchColumn;
      const matchEnd = searchMatch.matchColumn + searchMatch.matchLength;

      // Compute overlap between match and visible range
      const hlStart = Math.max(matchStart, visibleStart) - visibleStart;
      const hlEnd = Math.min(matchEnd, visibleEnd) - visibleStart;

      if (hlStart < hlEnd && hlStart >= 0 && hlEnd <= visibleText.length) {
        const before = visibleText.slice(0, hlStart);
        const highlighted = visibleText.slice(hlStart, hlEnd);
        const after = visibleText.slice(hlEnd);

        return (
          <pre
            className="content-line content-line--large"
            style={{ whiteSpace: 'pre', margin: 0, flex: 1, minWidth: 0 }}
          >{before}<span className="search-match-highlight" style={{ backgroundColor: '#515c6a', outline: '1px solid #c8c8c4' }}>{highlighted}</span>{after}</pre>
        );
      }
    }

    return (
      <pre
        className="content-line content-line--large"
        style={{ whiteSpace: 'pre', margin: 0, flex: 1, minWidth: 0 }}
      >{visibleText}</pre>
    );
  }

  // --- Task 8.4: Compute max line length across buffered lines ---
  const maxLineLength = React.useMemo(() => {
    if (!lineLengths || lineLengths.length === 0) return 0;
    let max = 0;
    for (const len of lineLengths) {
      if (len > max) max = len;
    }
    return max;
  }, [lineLengths]);

  // --- Early returns (after all hooks) ---

  if (isLoading) {
    return (
      <div className="content-area content-area--centered">
        <div className="content-area__loading">
          <div className="content-area__spinner" role="status" aria-label="Loading" />
          <p className="content-area__loading-text">Scanning file...</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="content-area content-area--centered">
        <div className="content-area__error">
          <span className="content-area__error-icon" aria-hidden="true">⚠</span>
          <p className="content-area__error-message">{error.message}</p>
        </div>
      </div>
    );
  }

  if (!fileMeta) {
    return (
      <div className="content-area content-area--centered">
        <p className="content-area__empty-prompt">Press Ctrl+O to open a file</p>
      </div>
    );
  }

  const showHScrollbar = !wrapLines && maxLineLength > H_VIEWPORT_CHARS;

  return (
    <div className="content-area content-area--virtual" style={{ display: 'flex', flexDirection: 'column', height: '100%', position: 'relative' }}>
      {/* Task 10.1: Copy loading indicator */}
      {copyLoading && (
        <div
          className="content-area__copy-loading"
          style={{
            position: 'absolute', top: 8, right: 8, zIndex: 100,
            background: 'rgba(30, 30, 30, 0.9)', color: '#fff',
            padding: '6px 12px', borderRadius: 4, fontSize: 12,
            display: 'flex', alignItems: 'center', gap: 6,
          }}
        >
          <div className="content-area__spinner" style={{ width: 12, height: 12 }} role="status" aria-label="Copying" />
          <span>Loading selection…</span>
        </div>
      )}
      {/* Task 10.1: Copy error toast */}
      {copyError && (
        <div
          className="content-area__copy-error"
          style={{
            position: 'absolute', top: 8, right: 8, zIndex: 100,
            background: 'rgba(180, 40, 40, 0.9)', color: '#fff',
            padding: '6px 12px', borderRadius: 4, fontSize: 12,
          }}
        >
          {copyError}
        </div>
      )}
      <div style={{ display: 'flex', flexDirection: 'row', flex: 1, minHeight: 0 }}>
        <div
          className="content-column content-column--hidden-scrollbar"
          ref={viewportRef}
          onScroll={handleScroll}
          style={{ overflowY: 'auto', overflowX: wrapLines ? 'hidden' : 'auto', flex: 1, minWidth: 0 }}
        >
          <div ref={containerRef}>
            {lines && lines.map((line: string, index: number) => (
              <div
                key={linesStartLine + index}
                className="line-container"
                style={{ display: 'flex', flexDirection: 'row', minHeight: LINE_HEIGHT }}
              >
                <div
                  className="line-number-row"
                  style={{
                    flexShrink: 0, width: 60, textAlign: 'right', paddingRight: 12,
                    alignSelf: 'flex-start', height: LINE_HEIGHT, lineHeight: `${LINE_HEIGHT}px`,
                  }}
                >
                  {linesStartLine + index + 1}
                </div>
                {renderLineContent(line, index, linesStartLine + index)}
              </div>
            ))}
          </div>
        </div>
        {React.createElement((window as any).CustomScrollbar, {
          key: 'scrollbar',
          range: fileMeta.totalLines,
          position: scrollbarPosition,
          viewportSize: SCROLLBAR_VIEWPORT_SIZE,
          onPositionChange: handleScrollbarDrag,
        })}
      </div>
      {showHScrollbar && (
        <div
          className="horizontal-scrollbar-container"
          style={{ display: 'flex', flexDirection: 'row', alignItems: 'center', padding: '2px 4px', flexShrink: 0 }}
        >
          {/* Offset to align with content (past line-number column) */}
          <div style={{ width: 60, flexShrink: 0 }} />
          <input
            type="range"
            className="horizontal-scrollbar"
            min={0}
            max={maxLineLength - H_VIEWPORT_CHARS}
            value={String(hScrollCol)}
            onChange={handleHScrollChange}
            style={{ flex: 1 }}
            aria-label="Horizontal scroll"
          />
        </div>
      )}
    </div>
  );
}

// Expose on window
(window as any).ContentArea = ContentArea;
