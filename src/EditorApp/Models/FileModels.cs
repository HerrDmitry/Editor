namespace EditorApp.Models;

/// <summary>
/// Result of a file open dialog operation.
/// </summary>
public record FileOpenResult(bool Success, string? FilePath, string? ErrorMessage);

/// <summary>
/// Metadata returned when a file is opened for streamed reading.
/// </summary>
public record FileOpenMetadata(string FilePath, string FileName, int TotalLines, long FileSizeBytes, string Encoding);

/// <summary>
/// Result of reading a range of lines from a file.
/// </summary>
public record LinesResult(int StartLine, string[] Lines, int TotalLines);
