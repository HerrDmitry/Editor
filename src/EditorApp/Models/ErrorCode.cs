namespace EditorApp.Models;

/// <summary>
/// Error codes for backend-to-frontend error communication.
/// </summary>
public enum ErrorCode
{
    FILE_NOT_FOUND,
    PERMISSION_DENIED,
    INTEROP_FAILURE,
    UNKNOWN_ERROR
}
