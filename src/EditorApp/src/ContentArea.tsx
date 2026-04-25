// ContentArea.tsx — compiled by tsc to wwwroot/js/ContentArea.js
// No module imports — React is a global from the UMD script.

interface FileContent {
  content: string;
  filePath: string;
  fileName: string;
  metadata: {
    fileSizeBytes: number;
    lineCount: number;
    encoding: string;
    lastModified: string;
  };
}

interface ErrorInfo {
  errorCode: string;
  message: string;
  details?: string;
}

interface ContentAreaProps {
  fileContent: FileContent | null;
  isLoading: boolean;
  error: ErrorInfo | null;
}

function ContentArea({ fileContent, isLoading, error }: ContentAreaProps) {
  // Loading state
  if (isLoading) {
    return (
      <div className="content-area content-area--centered">
        <div className="content-area__loading">
          <div className="content-area__spinner" role="status" aria-label="Loading" />
          <p className="content-area__loading-text">Loading file...</p>
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
  if (!fileContent) {
    return (
      <div className="content-area content-area--centered">
        <p className="content-area__empty-prompt">Press Ctrl+O to open a file</p>
      </div>
    );
  }

  // Content state — file loaded
  const lines = fileContent.content.split('\n');

  return (
    <div className="content-area content-area--file">
      <div className="content-area__line-numbers" aria-hidden="true">
        {lines.map((_: string, index: number) => (
          <div key={index} className="content-area__line-number">
            {index + 1}
          </div>
        ))}
      </div>
      <div className="content-area__content-lines">
        {lines.map((line: string, index: number) => (
          <pre key={index} className="content-area__content-line">
            {line}
          </pre>
        ))}
      </div>
    </div>
  );
}

// Expose on window
(window as any).ContentArea = ContentArea;
