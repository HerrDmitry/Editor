using EditorApp.Models;
using EditorApp.Services;

namespace EditorApp.Tests.Fixtures;

/// <summary>
/// Mock IMessageRouter for testing PhotinoHostService.
/// Records all messages sent to UI and registered handlers.
/// </summary>
public class MockMessageRouter : IMessageRouter
{
    private readonly Dictionary<string, Delegate> _handlers = new();

    /// <summary>
    /// All messages sent via SendToUIAsync, stored as (typeName, message).
    /// </summary>
    public List<(string TypeName, IMessage Message)> SentMessages { get; } = new();

    /// <summary>
    /// Only progress messages sent to UI.
    /// </summary>
    public List<FileLoadProgressMessage> ProgressMessages =>
        SentMessages
            .Where(m => m.Message is FileLoadProgressMessage)
            .Select(m => (FileLoadProgressMessage)m.Message)
            .ToList();

    /// <summary>
    /// Only error messages sent to UI.
    /// </summary>
    public List<ErrorResponse> ErrorMessages =>
        SentMessages
            .Where(m => m.Message is ErrorResponse)
            .Select(m => (ErrorResponse)m.Message)
            .ToList();

    public void RegisterHandler<TRequest>(Func<TRequest, Task> handler) where TRequest : IMessage
    {
        _handlers[typeof(TRequest).Name] = handler;
    }

    public Task SendToUIAsync<TMessage>(TMessage message) where TMessage : IMessage
    {
        SentMessages.Add((typeof(TMessage).Name, message));
        return Task.CompletedTask;
    }

    public void StartListening()
    {
        // No-op for testing
    }

    /// <summary>
    /// Simulate receiving a message from the frontend by invoking the registered handler.
    /// </summary>
    public async Task SimulateMessageAsync<TRequest>(TRequest request) where TRequest : IMessage
    {
        var typeName = typeof(TRequest).Name;
        if (_handlers.TryGetValue(typeName, out var handler))
        {
            await ((Func<TRequest, Task>)handler)(request);
        }
    }
}
