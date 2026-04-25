using Photino.Blazor;

namespace EditorApp.Services;

/// <summary>
/// Bridges the real Photino.Blazor window to the <see cref="IPhotinoWindowMessaging"/>
/// abstraction so that <see cref="MessageRouter"/> can be tested without a live window.
/// </summary>
public class PhotinoWindowMessagingAdapter : IPhotinoWindowMessaging
{
    private readonly PhotinoBlazorApp _app;

    public PhotinoWindowMessagingAdapter(PhotinoBlazorApp app)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
    }

    /// <inheritdoc />
    public void SendWebMessage(string message)
    {
        _app.MainWindow.SendWebMessage(message);
    }

    /// <inheritdoc />
    public void RegisterWebMessageReceivedHandler(Action<string> handler)
    {
        _app.MainWindow.RegisterWebMessageReceivedHandler((sender, message) =>
        {
            handler(message);
        });
    }
}
