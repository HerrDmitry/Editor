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
/// Payload sent to the frontend when a file is successfully loaded.
/// </summary>
public class FileLoadedResponse : IMessage
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public FileMetadataPayload Metadata { get; set; } = new();
}

/// <summary>
/// JSON-friendly metadata payload for interop messages.
/// </summary>
public class FileMetadataPayload
{
    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("lineCount")]
    public int LineCount { get; set; }

    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = string.Empty;

    [JsonPropertyName("lastModified")]
    public string LastModified { get; set; } = string.Empty;
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
/// Payload sent to the frontend as a warning (e.g., large file).
/// </summary>
public class WarningResponse : IMessage
{
    [JsonPropertyName("warningCode")]
    public string WarningCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("fileSizeBytes")]
    public long FileSizeBytes { get; set; }
}
