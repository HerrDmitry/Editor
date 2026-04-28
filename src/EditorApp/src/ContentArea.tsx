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

  // Refs for fresh values in callbacks
  const bufferRef = React.useRef({ start: linesStartLine, count: lines ? lines.length : 0 });
  bufferRef.current = { start: linesStartLine, count: lines ? lines.length : 0 };
  const fileMetaRef = React.useRef(fileMeta);
  fileMetaRef.current = fileMeta;

  // Track previous buffer to detect prepend/append
  const prevStartRef = React.useRef(linesStartLine);
  const prevCountRef = React.useRef(lines ? lines.length : 0);

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
        const trimCount = lines.length - WINDOW_SIZE;
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

  return (
    <div className="content-area content-area--virtual" style={{ display: 'flex', flexDirection: 'row' }}>
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
              <pre
                className="content-line"
                style={{
                  whiteSpace: wrapLines ? 'pre-wrap' : 'pre',
                  margin: 0, flex: 1, minWidth: 0,
                  wordBreak: wrapLines ? 'break-all' : 'normal',
                }}
              >{line}</pre>
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
  );
}

// Expose on window
(window as any).ContentArea = ContentArea;
