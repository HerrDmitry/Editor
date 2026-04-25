namespace EditorApp.Models;

/// <summary>
/// Result of a file open dialog operation.
/// </summary>
public record FileOpenResult(bool Success, string? FilePath, string? ErrorMessage);

/// <summary>
/// Metadata about a file including size, line count, encoding, and last modified date.
/// </summary>
public record FileMetadata(long FileSizeBytes, int LineCount, string Encoding, DateTime LastModified);

/// <summary>
/// Represents the full content and metadata of a loaded file.
/// </summary>
public record FileContent(string Content, string FilePath, string FileName, FileMetadata Metadata);
