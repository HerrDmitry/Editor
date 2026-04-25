// StatusBar.tsx — compiled by tsc to wwwroot/js/StatusBar.js
// No module imports — React is a global from the UMD script.

interface FileMetadata {
  fileSizeBytes: number;
  lineCount: number;
  encoding: string;
  lastModified: string;
}

interface StatusBarProps {
  metadata: FileMetadata | null | undefined;
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

function StatusBar({ metadata }: StatusBarProps) {
  return (
    <div className="status-bar" role="contentinfo">
      {metadata ? (
        <div className="status-bar__items">
          <span className="status-bar__item">{formatFileSize(metadata.fileSizeBytes)}</span>
          <span className="status-bar__separator" aria-hidden="true">|</span>
          <span className="status-bar__item">{metadata.lineCount} lines</span>
          <span className="status-bar__separator" aria-hidden="true">|</span>
          <span className="status-bar__item">{metadata.encoding}</span>
        </div>
      ) : null}
    </div>
  );
}

// Expose on window
(window as any).StatusBar = StatusBar;
(window as any).formatFileSize = formatFileSize;
