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
  const lastRequestedRef = React.useRef<{ startLine: number; lineCount: number }>({ startLine: -1, lineCount: -1 });
  const debounceRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleScroll = React.useCallback(() => {
    if (!fileMeta || !containerRef.current) return;

    // Debounce scroll requests to avoid flooding the backend
    if (debounceRef.current) {
      clearTimeout(debounceRef.current);
    }

    debounceRef.current = setTimeout(() => {
      if (!containerRef.current) return;

      const scrollTop = containerRef.current.scrollTop;
      const containerHeight = containerRef.current.clientHeight;

      const startLine = Math.floor(scrollTop / LINE_HEIGHT);
      const lineCount = Math.ceil(containerHeight / LINE_HEIGHT) + BUFFER_LINES;

      const last = lastRequestedRef.current;
      if (last.startLine === startLine && last.lineCount === lineCount) {
        return;
      }

      lastRequestedRef.current = { startLine, lineCount };
      onRequestLines(startLine, lineCount);
    }, 16); // ~1 frame at 60fps
  }, [fileMeta, onRequestLines]);

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

  // Content state — virtual scrolling
  const totalHeight = fileMeta.totalLines * LINE_HEIGHT;

  return (
    <div
      className="content-area content-area--virtual"
      ref={containerRef}
      onScroll={handleScroll}
      style={{ overflowY: 'auto', overflowX: 'auto', position: 'relative' }}
    >
      {/* Spacer creates full-height scrollbar */}
      <div style={{ height: totalHeight, position: 'relative' }}>
        {/* Visible lines positioned at correct offset */}
        {lines && (
          <div style={{ position: 'absolute', top: linesStartLine * LINE_HEIGHT, left: 0, right: 0 }}>
            {lines.map((line: string, index: number) => (
              <div
                key={linesStartLine + index}
                className="line-row"
                style={{ height: LINE_HEIGHT, display: 'flex', alignItems: 'center' }}
              >
                <span className="line-number" aria-hidden="true">
                  {linesStartLine + index + 1}
                </span>
                <pre className="content-line">{line}</pre>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

// Expose on window
(window as any).ContentArea = ContentArea;
