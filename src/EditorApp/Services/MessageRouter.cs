using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using EditorApp.Models;

namespace EditorApp.Services;

/// <summary>
/// Routes messages between the C# backend and the React frontend
/// via Photino's web message interop bridge.
/// </summary>
public class MessageRouter : IMessageRouter
{
    private readonly IPhotinoWindowMessaging _window;

    /// <summary>
    /// Maps message type names (e.g. "OpenFileRequest") to handler delegates.
    /// Each handler accepts a raw JSON string (the payload) and returns a Task.
    /// </summary>
    private readonly ConcurrentDictionary<string, Func<string, Task>> _handlers = new();

    /// <summary>
    /// Maps message type names to their CLR types for deserialization.
    /// </summary>
    private readonly ConcurrentDictionary<string, Type> _messageTypes = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public MessageRouter(IPhotinoWindowMessaging window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <inheritdoc />
    public void RegisterHandler<TRequest>(Func<TRequest, Task> handler) where TRequest : IMessage
    {
        ArgumentNullException.ThrowIfNull(handler);

        var typeName = typeof(TRequest).Name;

        _messageTypes[typeName] = typeof(TRequest);

        _handlers[typeName] = async (string rawJson) =>
        {
            TRequest? deserialized;

            if (string.IsNullOrEmpty(rawJson) || rawJson == "null")
            {
                // For messages with no payload (e.g. OpenFileRequest),
                // create a default instance.
                deserialized = Activator.CreateInstance<TRequest>();
            }
            else
            {
                deserialized = JsonSerializer.Deserialize<TRequest>(rawJson, SerializerOptions);
            }

            if (deserialized is null)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize payload for message type '{typeName}'.");
            }

            await handler(deserialized);
        };
    }

    /// <inheritdoc />
    public Task SendToUIAsync<TMessage>(TMessage message) where TMessage : IMessage
    {
        ArgumentNullException.ThrowIfNull(message);

        var envelope = new MessageEnvelope
        {
            Type = typeof(TMessage).Name,
            Payload = message,
            Timestamp = DateTime.UtcNow.ToString("o") // ISO 8601
        };

        var json = JsonSerializer.Serialize(envelope, SerializerOptions);
        _window.SendWebMessage(json);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void StartListening()
    {
        _window.RegisterWebMessageReceivedHandler(OnMessageReceived);
    }

    /// <summary>
    /// Callback invoked when the Photino window receives a web message.
    /// Deserializes the envelope, looks up the registered handler, and dispatches.
    /// </summary>
    internal void OnMessageReceived(string rawMessage)
    {
        _ = HandleMessageAsync(rawMessage);
    }

    /// <summary>
    /// Async message handling pipeline. Separated from the synchronous callback
    /// so that handler exceptions can be caught and logged.
    /// </summary>
    internal async Task HandleMessageAsync(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            return;
        }

        // Skip messages that aren't JSON objects (e.g. Blazor framework
        // messages starting with '_blazor' or other non-JSON strings).
        var trimmed = rawMessage.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return;
        }

        MessageEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<MessageEnvelope>(rawMessage, SerializerOptions);
        }
        catch (JsonException)
        {
            // Malformed JSON — nothing we can route. Silently ignore.
            return;
        }

        if (envelope is null || string.IsNullOrEmpty(envelope.Type))
        {
            return;
        }

        if (!_handlers.TryGetValue(envelope.Type, out var handler))
        {
            // No handler registered for this message type — ignore.
            return;
        }

        // The payload in the envelope is a JsonElement when deserialized as object?.
        // Re-serialize it so the typed handler can deserialize to the concrete type.
        string payloadJson;
        if (envelope.Payload is JsonElement element)
        {
            payloadJson = element.GetRawText();
        }
        else
        {
            payloadJson = envelope.Payload is null ? "null" : JsonSerializer.Serialize(envelope.Payload, SerializerOptions);
        }

        try
        {
            await handler(payloadJson);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"[ERROR] JSON serialization failure in message handler for '{envelope.Type}'\n{ex}");
            try
            {
                await SendToUIAsync(new ErrorResponse
                {
                    ErrorCode = Models.ErrorCode.INTEROP_FAILURE.ToString(),
                    Message = "An internal communication error occurred.",
                    Details = ex.Message
                });
            }
            catch
            {
                // If we can't even send the error response, there's nothing more we can do.
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Unhandled exception in message handler for '{envelope.Type}'\n{ex}");
        }
    }
}
