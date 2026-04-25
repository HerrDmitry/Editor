using EditorApp.Models;

namespace EditorApp.Services;

/// <summary>
/// Routes messages between the C# backend and the React frontend
/// via Photino's web message interop bridge.
/// </summary>
public interface IMessageRouter
{
    /// <summary>
    /// Register a typed handler that will be invoked when a message of the
    /// given type arrives from the frontend.
    /// </summary>
    void RegisterHandler<TRequest>(Func<TRequest, Task> handler) where TRequest : IMessage;

    /// <summary>
    /// Serialize a message as a <see cref="MessageEnvelope"/> and send it
    /// to the React UI via the Photino web message bridge.
    /// </summary>
    Task SendToUIAsync<TMessage>(TMessage message) where TMessage : IMessage;

    /// <summary>
    /// Wire up the Photino window's WebMessageReceived event so that
    /// incoming JSON messages are deserialized and routed to registered handlers.
    /// </summary>
    void StartListening();
}
