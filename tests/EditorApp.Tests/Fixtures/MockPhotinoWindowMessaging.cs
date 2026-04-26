using EditorApp.Services;

namespace EditorApp.Tests.Fixtures;

public class MockPhotinoWindowMessaging : IPhotinoWindowMessaging
{
    public List<string> SentMessages { get; } = new();
    private Action<string>? _handler;

    public void SendWebMessage(string message) => SentMessages.Add(message);
    public void RegisterWebMessageReceivedHandler(Action<string> handler) => _handler = handler;
    public void SimulateReceive(string message) => _handler?.Invoke(message);
}
