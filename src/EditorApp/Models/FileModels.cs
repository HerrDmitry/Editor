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
/// <param name="StartLine">Zero-based start line number.</param>
/// <param name="Lines">Array of line text content.</param>
/// <param name="TotalLines">Total number of lines in the file.</param>
/// <param name="LineLengths">Per-line character lengths. Null when all lines are normal (backward compat).</param>
public record LinesResult(int StartLine, string[] Lines, int TotalLines, int[]? LineLengths = null);

/// <summary>
/// Progress data reported during the file scanning phase via IProgress&lt;T&gt;.
/// </summary>
public record FileLoadProgress(string FileName, int Percent, long FileSizeBytes);

/// <summary>
/// Result of reading a chunk from a large line. Used for chunked line reading
/// where lines exceed <c>ChunkSizeChars</c> and must be delivered in pieces.
/// </summary>
/// <param name="LineNumber">Zero-based line number in the file.</param>
/// <param name="StartColumn">Zero-based character offset within the line where this chunk begins.</param>
/// <param name="Text">The chunk text content.</param>
/// <param name="TotalLineChars">Total character length of the entire line.</param>
/// <param name="HasMore">Whether more characters exist beyond this chunk.</param>
public record LineChunkResult(int LineNumber, int StartColumn, string Text, int TotalLineChars, bool HasMore);
