namespace EditorApp.Services;

/// <summary>
/// Abstraction over the Photino window's messaging capabilities.
/// Allows the MessageRouter to be tested without a real Photino window.
/// </summary>
public interface IPhotinoWindowMessaging
{
    /// <summary>
    /// Send a string message to the web view (React UI).
    /// </summary>
    void SendWebMessage(string message);

    /// <summary>
    /// Register a callback that is invoked when the web view sends a message.
    /// </summary>
    void RegisterWebMessageReceivedHandler(Action<string> handler);
}
