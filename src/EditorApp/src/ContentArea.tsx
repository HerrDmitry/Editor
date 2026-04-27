// ContentArea.tsx — compiled by tsc to wwwroot/js/ContentArea.js
// No module imports — React is a global from the UMD script.

/** Fixed line height in pixels for virtual scroll calculations. */
const LINE_HEIGHT = 20;

/** Number of extra lines to request beyond the visible viewport. */
const BUFFER_LINES = 10;

/** Fixed viewport size for scrollbar thumb sizing (logical lines). */
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
}

function ContentArea({ fileMeta, lines, linesStartLine, isLoading, error, wrapLines, onRequestLines }: ContentAreaProps) {
  const containerRef = React.useRef<HTMLDivElement>(null);
  const lineNumbersRef = React.useRef<HTMLDivElement>(null);
  const lastRequestedRef = React.useRef<{ startLine: number; lineCount: number }>({ startLine: -1, lineCount: -1 });
  const debounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  // Cap spacer height to avoid browser max element height limits (~33M pixels).
  const MAX_SCROLL_HEIGHT = 10_000_000;
  const rawTotalHeight = fileMeta ? fileMeta.totalLines * LINE_HEIGHT : 0;
  const totalHeight = Math.min(rawTotalHeight, MAX_SCROLL_HEIGHT);

  // Map native scrollTop to a line number using proportional mapping.
  const scrollTopToLine = (scrollTop: number, containerHeight: number): number => {
    if (!fileMeta || totalHeight <= containerHeight) return 0;
    const maxScrollTop = totalHeight - containerHeight;
    if (maxScrollTop <= 0) return 0;
    const scrollFraction = scrollTop / maxScrollTop;
    const maxLine = fileMeta.totalLines - Math.ceil(containerHeight / LINE_HEIGHT);
    return Math.round(scrollFraction * Math.max(0, maxLine));
  };

  const lineToScrollTop = (line: number, containerHeight: number): number => {
    if (!fileMeta || totalHeight <= containerHeight) return 0;
    const maxScrollTop = totalHeight - containerHeight;
    if (maxScrollTop <= 0) return 0;
    const maxLine = fileMeta.totalLines - Math.ceil(containerHeight / LINE_HEIGHT);
    if (maxLine <= 0) return 0;
    const scrollFraction = line / maxLine;
    return scrollFraction * maxScrollTop;
  };

  // All hooks must be called unconditionally before any early returns.

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
  }, [fileMeta, onRequestLines]);

  // Debounced scrollbar handler for wrap mode (requests lines directly from backend)
  const handleScrollbarWrap = React.useCallback((pos: number) => {
    if (!fileMeta) return;
    const targetLine = Math.max(0, Math.min(Math.round(pos), fileMeta.totalLines - 1));

    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
    }

    debounceRef.current = setTimeout(() => {
      if (targetLine !== lastRequestedRef.current.startLine) {
        const lineCount = SCROLLBAR_VIEWPORT_SIZE + BUFFER_LINES;
        lastRequestedRef.current = { startLine: targetLine, lineCount };
        onRequestLines(targetLine, lineCount);
      }
    }, 16);
  }, [fileMeta, onRequestLines]);

  // Wheel handler for wrap mode — translates mouse wheel into line navigation
  const handleWheelWrap = React.useCallback((e: React.WheelEvent<HTMLDivElement>) => {
    if (!fileMeta) return;
    e.preventDefault();
    
    // Each wheel tick scrolls ~3 lines
    const lineDelta = Math.sign(e.deltaY) * 3;
    const currentStart = lastRequestedRef.current.startLine >= 0 ? lastRequestedRef.current.startLine : linesStartLine;
    const targetLine = Math.max(0, Math.min(currentStart + lineDelta, fileMeta.totalLines - 1));
    
    if (targetLine !== lastRequestedRef.current.startLine) {
      if (debounceRef.current) {
        clearTimeout(debounceRef.current);
      }
      
      debounceRef.current = setTimeout(() => {
        const lineCount = SCROLLBAR_VIEWPORT_SIZE + BUFFER_LINES;
        lastRequestedRef.current = { startLine: targetLine, lineCount };
        onRequestLines(targetLine, lineCount);
      }, 16);
    }
  }, [fileMeta, linesStartLine, onRequestLines]);

  // Debounced scrollbar handler for non-wrap mode (scrolls the native container)
  const handleScrollbarNoWrap = React.useCallback((pos: number) => {
    if (!containerRef.current || !fileMeta) return;
    const targetLine = Math.max(0, Math.min(Math.round(pos), fileMeta.totalLines - 1));

    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
    }

    debounceRef.current = setTimeout(() => {
      handleScrollToLine(targetLine);
    }, 16);
  }, [fileMeta, handleScrollToLine]);

  // --- Early returns (after all hooks) ---

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

  // Calculate the position for the visible lines (non-wrap mode only)
  const linesPixelOffset = lineToScrollTop(linesStartLine, containerRef.current?.clientHeight || 800);
  const renderedLinesHeight = lines ? lines.length * LINE_HEIGHT : 0;
  const maxOffset = Math.max(0, totalHeight - renderedLinesHeight);
  const clampedOffset = Math.max(0, Math.min(linesPixelOffset, maxOffset));

  // --- Wrap mode ---
  if (wrapLines) {
    return (
      <div className="content-area content-area--virtual" style={{ display: 'flex', flexDirection: 'row' }}>
        <div
          className="content-column content-column--hidden-scrollbar"
          ref={containerRef}
          onWheel={handleWheelWrap}
          style={{ overflowY: 'hidden', overflowX: 'hidden', flex: 1, minWidth: 0 }}
        >
          {lines && lines.map((line: string, index: number) => (
            <div
              key={linesStartLine + index}
              className="line-container"
              style={{ display: 'flex', flexDirection: 'row', minHeight: LINE_HEIGHT, lineHeight: LINE_HEIGHT + 'px' }}
            >
              <div
                className="line-number-row"
                style={{
                  flexShrink: 0,
                  width: 60,
                  textAlign: 'right',
                  paddingRight: 12,
                  alignSelf: 'flex-start',
                  height: LINE_HEIGHT,
                  lineHeight: LINE_HEIGHT + 'px',
                }}
              >
                {linesStartLine + index + 1}
              </div>
              <pre
                className="content-line"
                style={{ whiteSpace: 'pre-wrap', margin: 0, flex: 1, minWidth: 0, wordBreak: 'break-all' }}
              >{line}</pre>
            </div>
          ))}
        </div>
        {React.createElement((window as any).CustomScrollbar, {
          key: 'scrollbar',
          range: fileMeta.totalLines,
          position: linesStartLine,
          viewportSize: SCROLLBAR_VIEWPORT_SIZE,
          onPositionChange: handleScrollbarWrap,
        })}
      </div>
    );
  }

  // --- Non-wrap mode ---
  return (
    <div className="content-area content-area--virtual" style={{ display: 'flex', flexDirection: 'row' }}>
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
                  style={{ height: LINE_HEIGHT, lineHeight: LINE_HEIGHT + 'px', margin: 0, whiteSpace: 'pre' }}
                >{line}</pre>
              ))}
            </div>
          )}
        </div>
      </div>
      {React.createElement((window as any).CustomScrollbar, {
        key: 'scrollbar',
        range: fileMeta.totalLines,
        position: linesStartLine,
        viewportSize: SCROLLBAR_VIEWPORT_SIZE,
        onPositionChange: handleScrollbarNoWrap,
      })}
    </div>
  );
}

// Expose on window
(window as any).ContentArea = ContentArea;
