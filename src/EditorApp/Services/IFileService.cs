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
    /// Get the character length of a specific line without reading its content.
    /// Derives length from CompressedLineIndex byte offsets. For lines with
    /// multibyte characters, the result is cached after first computation.
    /// </summary>
    /// <param name="filePath">Path to the opened file.</param>
    /// <param name="lineNumber">Zero-based line number.</param>
    /// <returns>Estimated character length of the line (excluding newline).</returns>
    int GetLineCharLength(string filePath, int lineNumber);

    /// <summary>
    /// Read a chunk of characters from a specific line using seek-based reading.
    /// The line is not loaded entirely into memory — only the requested portion is read.
    /// </summary>
    /// <param name="filePath">Path to the opened file.</param>
    /// <param name="lineNumber">Zero-based line number.</param>
    /// <param name="startColumn">Zero-based character offset within the line.</param>
    /// <param name="columnCount">Number of characters to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The chunk result with text and metadata.</returns>
    Task<LineChunkResult> ReadLineChunkAsync(string filePath, int lineNumber, int startColumn, int columnCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search for all occurrences of a term within a large line using chunked reading.
    /// Reads the line in chunks from disk, overlapping consecutive reads by at least
    /// <paramref name="searchTerm"/>.Length - 1 characters to detect boundary matches.
    /// Returns match positions as zero-based column offsets.
    /// </summary>
    /// <param name="filePath">Path to the opened file.</param>
    /// <param name="lineNumber">Zero-based line number to search within.</param>
    /// <param name="searchTerm">The term to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of zero-based column offsets where the search term occurs.</returns>
    Task<List<int>> SearchInLargeLineAsync(string filePath, int lineNumber, string searchTerm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the total number of physical lines in a previously opened file.
    /// </summary>
    int GetTotalLines(string filePath);

    /// <summary>
    /// Get the maximum character length across all lines in a previously opened file.
    /// Computed during file scan and stored in cache metadata.
    /// </summary>
    int GetMaxLineLength(string filePath);

    /// <summary>
    /// Remove a file from the line index cache and dispose its resources.
    /// No-op if the file is not in the cache.
    /// </summary>
    void CloseFile(string filePath);
}
