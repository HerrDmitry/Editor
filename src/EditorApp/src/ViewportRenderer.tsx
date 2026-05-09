// ViewportRenderer.tsx — viewport-based rendering component
// Replaces ContentArea for content display when viewport system is active.
// Uses fixed-width font metrics, oversized buffer, smooth scrolling.

interface ViewportFileMeta {
  totalLines: number;
  maxLineLength: number;
  totalVirtualLines?: number;
}

interface ViewportResponsePayload {
  lines: string[];
  startLine: number;
  startColumn: number;
  totalPhysicalLines: number;
  lineLengths: number[];
  maxLineLength: number;
  totalVirtualLines: number | null;
  truncated: boolean;
}

interface BufferState {
  lines: string[];
  startLine: number;
  startCol: number;
  lineCount: number;
  colCount: number;
}

interface ScrollState {
  line: number;
  column: number;
  subPixelY: number;
  subPixelX: number;
}

interface CharCell {
  width: number;
  height: number;
}

interface ViewportDimensions {
  rows: number;
  columns: number;
}

interface ViewportRendererProps {
  fileMeta: ViewportFileMeta | null;
  wrapMode: boolean;
}

// ─── Utility: Char Cell Measurement ───────────────────────────────────────────

/** Measure monospace char cell once. Returns {width, height} in px. */
function measureCharCell(): CharCell {
  const el = document.createElement('span');
  el.style.font = '14px monospace';
  el.style.position = 'absolute';
  el.style.visibility = 'hidden';
  el.style.whiteSpace = 'pre';
  el.textContent = 'M';
  document.body.appendChild(el);
  const rect = el.getBoundingClientRect();
  document.body.removeChild(el);
  // Fallback if measurement fails
  const width = rect.width > 0 ? rect.width : 8;
  const height = rect.height > 0 ? rect.height : 16;
  return { width, height };
}

// ─── Utility: Viewport Dimensions ─────────────────────────────────────────────

function computeViewportDimensions(
  containerWidth: number,
  containerHeight: number,
  cell: CharCell
): ViewportDimensions {
  return {
    rows: Math.floor(containerHeight / cell.height),
    columns: Math.floor(containerWidth / cell.width),
  };
}

// ─── Utility: Scrollbar Computation ───────────────────────────────────────────

function computeScrollbarThumb(
  visibleUnits: number,
  totalUnits: number,
  currentOffset: number,
  trackSize: number
): { size: number; position: number } {
  if (totalUnits <= 0 || visibleUnits >= totalUnits) {
    return { size: trackSize, position: 0 };
  }
  const size = Math.max(20, (visibleUnits / totalUnits) * trackSize);
  const position = (currentOffset / totalUnits) * trackSize;
  return { size, position };
}

function computeTargetFromThumbFraction(fraction: number, totalUnits: number): number {
  return Math.floor(fraction * totalUnits);
}

// ─── Utility: Buffer Management ───────────────────────────────────────────────

/** Determine if target is within current buffer bounds. */
function isWithinBuffer(target: number, buffer: BufferState): boolean {
  return target >= buffer.startLine && target < buffer.startLine + buffer.lineCount;
}

/** Determine if prefetch should trigger (remaining < 25% of buffer in scroll direction). */
function shouldPrefetch(
  scrollLine: number,
  buffer: BufferState,
  scrollDirection: 'down' | 'up'
): boolean {
  const threshold = buffer.lineCount * 0.25;
  if (scrollDirection === 'down') {
    const remaining = (buffer.startLine + buffer.lineCount) - scrollLine;
    return remaining < threshold;
  } else {
    const remaining = scrollLine - buffer.startLine;
    return remaining < threshold;
  }
}

/** Compute oversized buffer request (2× viewport). */
function computeBufferRequest(
  viewportRows: number,
  viewportCols: number
): { lineCount: number; colCount: number } {
  return {
    lineCount: viewportRows * 2,
    colCount: viewportCols * 2,
  };
}

// ─── Main Component ───────────────────────────────────────────────────────────

function ViewportRenderer({ fileMeta, wrapMode }: ViewportRendererProps) {
  const containerRef = React.useRef<HTMLDivElement>(null);
  const cellRef = React.useRef<CharCell | null>(null);
  const [dimensions, setDimensions] = React.useState<ViewportDimensions>({ rows: 0, columns: 0 });
  const [buffer, setBuffer] = React.useState<BufferState>({
    lines: [], startLine: 0, startCol: 0, lineCount: 0, colCount: 0
  });
  const [scroll, setScroll] = React.useState<ScrollState>({
    line: 0, column: 0, subPixelY: 0, subPixelX: 0
  });
  const [totalVirtualLines, setTotalVirtualLines] = React.useState<number | null>(null);
  const pendingRequestRef = React.useRef(false);
  const debounceTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  // Measure char cell once
  React.useEffect(() => {
    cellRef.current = measureCharCell();
  }, []);

  // Compute dimensions on mount + resize
  React.useEffect(() => {
    const container = containerRef.current;
    if (!container || !cellRef.current) return;

    const updateDimensions = () => {
      const rect = container.getBoundingClientRect();
      const dims = computeViewportDimensions(rect.width, rect.height, cellRef.current!);
      setDimensions(dims);
    };

    updateDimensions();

    const observer = new ResizeObserver(updateDimensions);
    observer.observe(container);
    return () => observer.disconnect();
  }, [fileMeta]);

  // Send viewport request
  const sendViewportRequest = React.useCallback((
    startLine: number, lineCount: number, startColumn: number, columnCount: number
  ) => {
    if (pendingRequestRef.current) return;
    pendingRequestRef.current = true;

    const envelope = {
      type: 'RequestViewport',
      payload: {
        startLine,
        lineCount,
        startColumn,
        columnCount,
        wrapMode,
        viewportColumns: dimensions.columns,
      },
      timestamp: new Date().toISOString(),
    };

    try {
      (window as any).external.sendMessage(JSON.stringify(envelope));
    } catch {
      pendingRequestRef.current = false;
    }
  }, [wrapMode, dimensions.columns]);

  // Handle viewport response
  React.useEffect(() => {
    function handleMessage(rawData: string) {
      let envelope: any;
      try { envelope = JSON.parse(rawData); } catch { return; }

      if (envelope.type === 'ViewportResponse') {
        const payload = envelope.payload as ViewportResponsePayload;
        pendingRequestRef.current = false;

        setBuffer({
          lines: payload.lines,
          startLine: payload.startLine,
          startCol: payload.startColumn,
          lineCount: payload.lines.length,
          colCount: dimensions.columns * 2,
        });

        if (payload.totalVirtualLines !== null) {
          setTotalVirtualLines(payload.totalVirtualLines);
        }
      }
    }

    if ((window as any).external && 'receiveMessage' in (window as any).external) {
      (window as any).external.receiveMessage(handleMessage);
    }
    window.addEventListener('message', (e: MessageEvent) => {
      const data = typeof e.data === 'string' ? e.data : JSON.stringify(e.data);
      handleMessage(data);
    });
  }, [dimensions.columns]);

  // Initial viewport request when file opens or dimensions change
  React.useEffect(() => {
    if (!fileMeta || dimensions.rows === 0 || dimensions.columns === 0) return;

    const { lineCount, colCount } = computeBufferRequest(dimensions.rows, dimensions.columns);
    sendViewportRequest(0, lineCount, 0, colCount);
  }, [fileMeta, dimensions.rows, dimensions.columns, sendViewportRequest]);

  // Wheel handler — smooth scrolling
  const handleWheel = React.useCallback((e: React.WheelEvent) => {
    e.preventDefault();
    if (!fileMeta || !cellRef.current) return;

    const cell = cellRef.current;
    const totalLines = wrapMode && totalVirtualLines
      ? totalVirtualLines
      : fileMeta.totalLines;

    setScroll(prev => {
      let newSubPixelY = prev.subPixelY + e.deltaY;
      let newLine = prev.line;
      let newSubPixelX = prev.subPixelX + e.deltaX;
      let newColumn = prev.column;

      // Vertical: cross cell boundaries
      while (newSubPixelY >= cell.height) {
        newSubPixelY -= cell.height;
        newLine++;
      }
      while (newSubPixelY < 0) {
        newSubPixelY += cell.height;
        newLine--;
      }

      // Horizontal: cross cell boundaries (no-wrap only)
      if (!wrapMode) {
        while (newSubPixelX >= cell.width) {
          newSubPixelX -= cell.width;
          newColumn++;
        }
        while (newSubPixelX < 0) {
          newSubPixelX += cell.width;
          newColumn--;
        }
      }

      // Clamp
      newLine = Math.max(0, Math.min(newLine, totalLines - dimensions.rows));
      newColumn = Math.max(0, Math.min(newColumn, fileMeta.maxLineLength - dimensions.columns));

      return { line: newLine, column: newColumn, subPixelY: newSubPixelY, subPixelX: newSubPixelX };
    });
  }, [fileMeta, wrapMode, totalVirtualLines, dimensions]);

  // Prefetch on scroll position change
  React.useEffect(() => {
    if (!fileMeta || buffer.lineCount === 0) return;

    const scrollDir = scroll.line > buffer.startLine + buffer.lineCount / 2 ? 'down' : 'up';
    if (shouldPrefetch(scroll.line, buffer, scrollDir)) {
      const { lineCount, colCount } = computeBufferRequest(dimensions.rows, dimensions.columns);
      const requestStart = Math.max(0, scroll.line - dimensions.rows);
      sendViewportRequest(requestStart, lineCount, scroll.column, colCount);
    }
  }, [scroll.line, scroll.column]);

  // Scrollbar thumb drag — vertical
  const handleVerticalDrag = React.useCallback((fraction: number) => {
    if (!fileMeta) return;
    const totalLines = wrapMode && totalVirtualLines ? totalVirtualLines : fileMeta.totalLines;
    const targetLine = computeTargetFromThumbFraction(fraction, totalLines);

    // Debounce
    if (debounceTimerRef.current) clearTimeout(debounceTimerRef.current);
    debounceTimerRef.current = setTimeout(() => {
      setScroll(prev => ({ ...prev, line: targetLine, subPixelY: 0 }));

      if (!isWithinBuffer(targetLine, buffer)) {
        // Full buffer replacement
        const { lineCount, colCount } = computeBufferRequest(dimensions.rows, dimensions.columns);
        const requestStart = Math.max(0, targetLine - dimensions.rows);
        sendViewportRequest(requestStart, lineCount, scroll.column, colCount);
      }
    }, 16);
  }, [fileMeta, wrapMode, totalVirtualLines, buffer, dimensions, scroll.column, sendViewportRequest]);

  // Scrollbar thumb drag — horizontal
  const handleHorizontalDrag = React.useCallback((fraction: number) => {
    if (!fileMeta || wrapMode) return;
    const targetCol = computeTargetFromThumbFraction(fraction, fileMeta.maxLineLength);

    if (debounceTimerRef.current) clearTimeout(debounceTimerRef.current);
    debounceTimerRef.current = setTimeout(() => {
      setScroll(prev => ({ ...prev, column: targetCol, subPixelX: 0 }));

      const { lineCount, colCount } = computeBufferRequest(dimensions.rows, dimensions.columns);
      sendViewportRequest(scroll.line, lineCount, targetCol, colCount);
    }, 16);
  }, [fileMeta, wrapMode, dimensions, scroll.line, sendViewportRequest]);

  // Render
  if (!fileMeta) {
    return React.createElement('div', { className: 'viewport-renderer', ref: containerRef },
      React.createElement('div', { className: 'viewport-empty' }, 'No file open')
    );
  }

  const totalLines = wrapMode && totalVirtualLines ? totalVirtualLines : fileMeta.totalLines;
  const cell = cellRef.current || { width: 8, height: 16 };
  const trackHeight = dimensions.rows * cell.height;
  const trackWidth = dimensions.columns * cell.width;

  const vThumb = computeScrollbarThumb(dimensions.rows, totalLines, scroll.line, trackHeight);
  const hThumb = !wrapMode
    ? computeScrollbarThumb(dimensions.columns, fileMeta.maxLineLength, scroll.column, trackWidth)
    : null;

  // Visible lines from buffer
  const bufferOffset = scroll.line - buffer.startLine;
  const visibleLines = buffer.lines.slice(
    Math.max(0, bufferOffset),
    Math.max(0, bufferOffset) + dimensions.rows
  );

  const transformStyle = {
    transform: `translate(${-scroll.subPixelX}px, ${-scroll.subPixelY}px)`,
    willChange: 'transform' as const,
  };

  return React.createElement('div', {
    className: 'viewport-renderer',
    ref: containerRef,
    onWheel: handleWheel,
  },
    // Content area with CSS transform for sub-pixel scrolling
    React.createElement('div', { className: 'viewport-content', style: transformStyle },
      visibleLines.map((line, i) =>
        React.createElement('div', {
          key: scroll.line + i,
          className: 'viewport-line',
          style: { height: cell.height + 'px' },
        }, line || '\u00A0')
      )
    ),
    // Vertical scrollbar
    React.createElement('div', { className: 'viewport-scrollbar-v' },
      React.createElement('div', {
        className: 'viewport-scrollbar-thumb',
        style: { height: vThumb.size + 'px', top: vThumb.position + 'px' },
      })
    ),
    // Horizontal scrollbar (no-wrap only)
    !wrapMode && hThumb && React.createElement('div', { className: 'viewport-scrollbar-h' },
      React.createElement('div', {
        className: 'viewport-scrollbar-thumb',
        style: { width: hThumb.size + 'px', left: hThumb.position + 'px' },
      })
    )
  );
}

// Expose utilities for testing
(window as any).ViewportRenderer = ViewportRenderer;
(window as any).measureCharCell = measureCharCell;
(window as any).computeViewportDimensions = computeViewportDimensions;
(window as any).computeScrollbarThumb = computeScrollbarThumb;
(window as any).computeTargetFromThumbFraction = computeTargetFromThumbFraction;
(window as any).isWithinBuffer = isWithinBuffer;
(window as any).shouldPrefetch = shouldPrefetch;
(window as any).computeBufferRequest = computeBufferRequest;
