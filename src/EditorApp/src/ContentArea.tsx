// ContentArea.tsx — compiled by tsc to wwwroot/js/ContentArea.js
// No module imports — React is a global from the UMD script.

/** Fixed line height in pixels for virtual scroll calculations. */
const LINE_HEIGHT = 20;

/** Number of extra lines to request beyond the visible viewport. */
const BUFFER_LINES = 10;

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
  onRequestLines: (startLine: number, lineCount: number) => void;
}

function ContentArea({ fileMeta, lines, linesStartLine, isLoading, error, onRequestLines }: ContentAreaProps) {
  const containerRef = React.useRef<HTMLDivElement>(null);
  const lineNumbersRef = React.useRef<HTMLDivElement>(null);
  const lastRequestedRef = React.useRef<{ startLine: number; lineCount: number }>({ startLine: -1, lineCount: -1 });
  const debounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const [currentTopLine, setCurrentTopLine] = React.useState(0);

  // Cap spacer height to avoid browser max element height limits (~33M pixels).
  const MAX_SCROLL_HEIGHT = 10_000_000;
  const rawTotalHeight = fileMeta ? fileMeta.totalLines * LINE_HEIGHT : 0;
  const totalHeight = Math.min(rawTotalHeight, MAX_SCROLL_HEIGHT);
  const scrollScale = rawTotalHeight > MAX_SCROLL_HEIGHT ? rawTotalHeight / MAX_SCROLL_HEIGHT : 1;

  const visibleLineCount = containerRef.current
    ? Math.ceil(containerRef.current.clientHeight / LINE_HEIGHT)
    : 50;

  // Map native scrollTop to a line number using proportional mapping.
  // This correctly handles the capped scroll height for large files.
  const scrollTopToLine = (scrollTop: number, containerHeight: number): number => {
    if (!fileMeta || totalHeight <= containerHeight) return 0;
    const maxScrollTop = totalHeight - containerHeight;
    if (maxScrollTop <= 0) return 0;
    const scrollFraction = scrollTop / maxScrollTop; // 0..1
    const maxLine = fileMeta.totalLines - Math.ceil(containerHeight / LINE_HEIGHT);
    return Math.round(scrollFraction * Math.max(0, maxLine));
  };

  const lineToScrollTop = (line: number, containerHeight: number): number => {
    if (!fileMeta || totalHeight <= containerHeight) return 0;
    const maxScrollTop = totalHeight - containerHeight;
    if (maxScrollTop <= 0) return 0;
    const maxLine = fileMeta.totalLines - Math.ceil(containerHeight / LINE_HEIGHT);
    if (maxLine <= 0) return 0;
    const scrollFraction = line / maxLine; // 0..1
    return scrollFraction * maxScrollTop;
  };

  const handleScrollToLine = React.useCallback((targetLine: number) => {
    if (!containerRef.current || !fileMeta) return;
    const clamped = Math.max(0, Math.min(targetLine, fileMeta.totalLines - 1));
    containerRef.current.scrollTop = lineToScrollTop(clamped, containerRef.current.clientHeight);
  }, [fileMeta, totalHeight]);

  const handleScroll = React.useCallback(() => {
    if (!fileMeta || !containerRef.current) return;

    // Sync line numbers vertical scroll with content scroll
    if (lineNumbersRef.current) {
      lineNumbersRef.current.scrollTop = containerRef.current.scrollTop;
    }

    // Map scroll position to line number using proportional mapping
    const visibleStartLine = scrollTopToLine(containerRef.current.scrollTop, containerRef.current.clientHeight);
    setCurrentTopLine(visibleStartLine);

    // Debounce line requests to avoid flooding the backend
    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
    }

    debounceRef.current = setTimeout(() => {
      if (!containerRef.current) return;

      const startLine = scrollTopToLine(containerRef.current.scrollTop, containerRef.current.clientHeight);
      const lineCount = Math.ceil(containerRef.current.clientHeight / LINE_HEIGHT) + BUFFER_LINES;

      const last = lastRequestedRef.current;
      if (last.startLine === startLine && last.lineCount === lineCount) {
        return;
      }

      lastRequestedRef.current = { startLine, lineCount };
      onRequestLines(startLine, lineCount);
    }, 16);
  }, [fileMeta, onRequestLines, scrollScale]);

  // Loading state
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

  // Error state
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

  // Empty state — no file loaded
  if (!fileMeta) {
    return (
      <div className="content-area content-area--centered">
        <p className="content-area__empty-prompt">Press Ctrl+O to open a file</p>
      </div>
    );
  }

  // Calculate the position for the visible lines
  const linesPixelOffset = fileMeta ? lineToScrollTop(linesStartLine, containerRef.current?.clientHeight || 800) : 0;
  const renderedLinesHeight = lines ? lines.length * LINE_HEIGHT : 0;
  const maxOffset = Math.max(0, totalHeight - renderedLinesHeight);
  const clampedOffset = Math.max(0, Math.min(linesPixelOffset, maxOffset));

  return (
    <div className="content-area content-area--virtual" style={{ display: 'flex', flexDirection: 'row' }}>
      {/* Line numbers column — scrolls vertically only, never horizontally */}
      <div
        className="line-numbers-column"
        ref={lineNumbersRef}
        style={{ overflowY: 'hidden', overflowX: 'hidden', flexShrink: 0, width: 60 }}
      >
        <div style={{ height: totalHeight, position: 'relative' }}>
          {lines && (
            <div style={{ position: 'absolute', top: clampedOffset, left: 0, right: 0 }}>
              {lines.map((_: string, index: number) => (
                <div
                  key={linesStartLine + index}
                  className="line-number-row"
                  style={{ height: LINE_HEIGHT, lineHeight: LINE_HEIGHT + 'px', textAlign: 'right', paddingRight: 12 }}
                >
                  {linesStartLine + index + 1}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
      {/* Content column — scrolls both vertically and horizontally */}
      <div
        className="content-column content-column--hidden-scrollbar"
        ref={containerRef}
        onScroll={handleScroll}
        style={{ overflowY: 'auto', overflowX: 'auto', flex: 1, minWidth: 0 }}
      >
        <div style={{ height: totalHeight, position: 'relative' }}>
          {lines && (
            <div style={{ position: 'absolute', top: clampedOffset, left: 0 }}>
              {lines.map((line: string, index: number) => (
                <pre
                  key={linesStartLine + index}
                  className="content-line"
                  style={{ height: LINE_HEIGHT, lineHeight: LINE_HEIGHT + 'px', margin: 0 }}
                >{line}</pre>
              ))}
            </div>
          )}
        </div>
      </div>
      {/* Custom scrollbar column */}
      {React.createElement((window as any).CustomScrollbar, {
        totalLines: fileMeta.totalLines,
        visibleLineCount,
        currentTopLine,
        onScrollToLine: handleScrollToLine,
      })}
    </div>
  );
}

// Expose on window
(window as any).ContentArea = ContentArea;
