namespace EditorApp.Services;

using EditorApp.Models;

/// <summary>
/// Abstraction for file system operations including streamed reading
/// with line offset indexing and encoding detection.
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Display the native OS file picker dialog and return the selected file path.
    /// </summary>
    Task<FileOpenResult> OpenFileDialogAsync();

    /// <summary>
    /// Scan a file to build a line offset index, count total lines, and detect encoding.
    /// For large files (>256KB), invokes <paramref name="onPartialMetadata"/> exactly once
    /// after the first 256KB has been scanned, providing provisional metadata so the UI
    /// can display content immediately while scanning continues.
    /// Reports progress for large files via the optional <paramref name="progress"/> callback.
    /// Returns final metadata only — no file content is loaded into memory.
    /// </summary>
    Task<FileOpenMetadata> OpenFileAsync(
        string filePath,
        Action<FileOpenMetadata>? onPartialMetadata = null,
        IProgress<FileLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-scan a previously opened file to rebuild its line index after external modification.
    /// Old CacheEntry remains valid for reads until new index is ready.
    /// </summary>
    Task<FileOpenMetadata> RefreshFileAsync(
        string filePath,
        IProgress<FileLoadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read a range of lines from a previously opened file using the line offset index.
    /// </summary>
    Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a file from the line index cache and dispose its resources.
    /// No-op if the file is not in the cache.
    /// </summary>
    void CloseFile(string filePath);
}
