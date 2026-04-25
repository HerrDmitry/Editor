namespace EditorApp.Services;

using EditorApp.Models;

/// <summary>
/// Abstraction for file system operations including reading files,
/// validating sizes, and detecting encoding.
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Display the native OS file picker dialog and return the selected file path.
    /// </summary>
    Task<FileOpenResult> OpenFileDialogAsync();

    /// <summary>
    /// Read a file from disk with encoding detection and return its content and metadata.
    /// </summary>
    Task<FileContent> ReadFileAsync(string filePath);

    /// <summary>
    /// Extract metadata (size, line count, encoding, last modified) for a file.
    /// </summary>
    Task<FileMetadata> GetFileMetadataAsync(string filePath);

    /// <summary>
    /// Validate whether a file size is within acceptable limits.
    /// Returns true if the file can be loaded. Sets warningMessage when
    /// the file is between 10 MB and 50 MB. Returns false when the file exceeds 50 MB.
    /// </summary>
    bool ValidateFileSize(long fileSize, out string? warningMessage);
}
