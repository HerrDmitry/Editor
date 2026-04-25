import type { FileMetadata } from '../types/messages';
import './StatusBar.css';

export interface StatusBarProps {
  metadata: FileMetadata | null | undefined;
}

/**
 * Format a file size in bytes to a human-readable string.
 *
 * - Sizes < 1024 → "X bytes"
 * - Sizes >= 1024 and < 1048576 → "X.Y KB"
 * - Sizes >= 1048576 → "X.Y MB"
 */
export function formatFileSize(bytes: number): string {
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

export default StatusBar;
