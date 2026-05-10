using System.Text.Json.Serialization;

namespace EditorApp.Models;

/// <summary>
/// Common envelope for all messages exchanged between backend and frontend.
/// </summary>
public class MessageEnvelope
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;
}

/// <summary>
/// Request from the frontend to open a file via the native file picker.
/// </summary>
public class OpenFileRequest : IMessage
{
}

/// <summary>
/// Request from the frontend for a range of lines from the currently open file.
/// </summary>
public class RequestLinesMessage : IMessage
{
    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("lineCount")]
    public int LineCount { get; set; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; set; }

    [JsonPropertyName("columnCount")]
    public int ColumnCount { get; set; }
}

/// <summary>
/// Sent to the frontend when a file is opened — metadata only, no content.
/// </summary>
public class FileOpenedResponse : IMessage
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("totalLines")]
    public int TotalLines { get; set; }

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = string.Empty;

    [JsonPropertyName("isPartial")]
    public bool IsPartial { get; set; }

    [JsonPropertyName("isRefresh")]
    public bool IsRefresh { get; set; }

    [JsonPropertyName("maxLineLength")]
    public int MaxLineLength { get; set; }
}

/// <summary>
/// Sent to the frontend with the requested range of lines.
/// </summary>
public class LinesResponse : IMessage
{
    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("lines")]
    public string[] Lines { get; set; } = Array.Empty<string>();

    [JsonPropertyName("totalLines")]
    public int TotalLines { get; set; }

    /// <summary>
    /// Character length of each line. For normal lines, equals Lines[i].Length.
    /// For large lines, indicates total line length (Lines[i] is truncated to first chunk).
    /// Null when all lines are normal (backward compat).
    /// </summary>
    [JsonPropertyName("lineLengths")]
    public int[]? LineLengths { get; set; }
}

/// <summary>
/// Payload sent to the frontend when an error occurs.
/// </summary>
public class ErrorResponse : IMessage
{
    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string? Details { get; set; }
}

/// <summary>
/// Progress message sent to the frontend during the file scanning phase for large files.
/// </summary>
public class FileLoadProgressMessage : IMessage
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("percent")]
    public int Percent { get; set; }

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }
}

/// <summary>
/// Request from frontend for a chunk of a large line.
/// </summary>
public class RequestLineChunk : IMessage
{
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; set; }

    [JsonPropertyName("columnCount")]
    public int ColumnCount { get; set; }
}

/// <summary>
/// Response with a chunk of line content.
/// </summary>
public class LineChunkResponse : IMessage
{
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; set; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("totalLineChars")]
    public int TotalLineChars { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }
}

/// <summary>
/// Request from the frontend for a viewport of text content with optional word-wrap support.
/// </summary>
public class RequestViewport : IMessage
{
    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("lineCount")]
    public int LineCount { get; set; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; set; }

    [JsonPropertyName("columnCount")]
    public int ColumnCount { get; set; }

    [JsonPropertyName("wrapMode")]
    public bool WrapMode { get; set; }

    [JsonPropertyName("viewportColumns")]
    public int ViewportColumns { get; set; }
}

/// <summary>
/// Response containing viewport text content and metadata for scrollbar computation.
/// </summary>
public class ViewportResponse : IMessage
{
    [JsonPropertyName("lines")]
    public string[] Lines { get; set; } = Array.Empty<string>();

    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; set; }

    [JsonPropertyName("totalPhysicalLines")]
    public int TotalPhysicalLines { get; set; }

    [JsonPropertyName("lineLengths")]
    public int[] LineLengths { get; set; } = Array.Empty<int>();

    [JsonPropertyName("maxLineLength")]
    public int MaxLineLength { get; set; }

    [JsonPropertyName("totalVirtualLines")]
    public int? TotalVirtualLines { get; set; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}
