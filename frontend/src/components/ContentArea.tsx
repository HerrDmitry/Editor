import type { FileContent, ErrorInfo } from '../types/messages';
import './ContentArea.css';

export interface ContentAreaProps {
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
        {lines.map((_, index) => (
          <div key={index} className="content-area__line-number">
            {index + 1}
          </div>
        ))}
      </div>
      <div className="content-area__content-lines">
        {lines.map((line, index) => (
          <pre key={index} className="content-area__content-line">
            {line}
          </pre>
        ))}
      </div>
    </div>
  );
}

export default ContentArea;
