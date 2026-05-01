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
