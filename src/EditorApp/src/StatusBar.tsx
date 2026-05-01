// StatusBar.tsx — compiled by tsc to wwwroot/js/StatusBar.js
// No module imports — React is a global from the UMD script.

interface FileMeta {
  totalLines: number;
  fileSizeBytes: number;
  encoding: string;
}

interface FileLoadProgressPayload {
  fileName: string;
  percent: number;
  fileSizeBytes: number;
}

interface StatusBarProps {
  metadata: FileMeta | null | undefined;
  wrapLines: boolean;
  onWrapLinesChange: (enabled: boolean) => void;
  loadProgress?: FileLoadProgressPayload | null;
}

/**
 * Format a file size in bytes to a human-readable string.
 *
 * - Sizes < 1024 → "X bytes"
 * - Sizes >= 1024 and < 1048576 → "X.Y KB"
 * - Sizes >= 1048576 → "X.Y MB"
 */
function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} bytes`;
  }
  if (bytes < 1048576) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }
  return `${(bytes / 1048576).toFixed(1)} MB`;
}

function StatusBar({ metadata, wrapLines, onWrapLinesChange, loadProgress }: StatusBarProps) {
  const showProgress = loadProgress != null && loadProgress.percent < 100;

  return (
    <div className="status-bar" role="contentinfo">
      {metadata ? (
        <div className="status-bar__items">
          <span className="status-bar__item">{formatFileSize(metadata.fileSizeBytes)}</span>
          <span className="status-bar__separator" aria-hidden="true">|</span>
          <span className="status-bar__item">{metadata.totalLines} lines</span>
          <span className="status-bar__separator" aria-hidden="true">|</span>
          <span className="status-bar__item">{metadata.encoding}</span>
        </div>
      ) : null}
      {showProgress ? (
        <div
          className="progress-bar"
          role="progressbar"
          aria-valuenow={loadProgress.percent}
          aria-valuemin={0}
          aria-valuemax={100}
        >
          <div
            className="progress-bar__fill"
            style={{ width: `${loadProgress.percent}%` }}
          />
          <span className="progress-bar__text">Loading: {loadProgress.percent}%</span>
        </div>
      ) : null}
      {!showProgress ? (
        <label className="status-bar__wrap-toggle">
          <input
            type="checkbox"
            className="status-bar__wrap-checkbox"
            checked={wrapLines}
            onChange={(e) => onWrapLinesChange(e.target.checked)}
          />
          Wrap Lines
        </label>
      ) : null}
    </div>
  );
}

// Expose on window
(window as any).StatusBar = StatusBar;
(window as any).formatFileSize = formatFileSize;
