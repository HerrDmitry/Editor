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
    /// Returns metadata only — no file content is loaded into memory.
    /// </summary>
    Task<FileOpenMetadata> OpenFileAsync(string filePath);

    /// <summary>
    /// Read a range of lines from a previously opened file using the line offset index.
    /// </summary>
    Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount);
}
