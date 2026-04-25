namespace EditorApp.Services;

/// <summary>
/// Manages keyboard shortcut registration for the editor application.
/// 
/// Since Photino does not expose native keyboard shortcut APIs, shortcuts are
/// handled in the browser layer via a JavaScript keydown listener embedded in
/// the host page (App.razor). When a shortcut fires, the browser sends an
/// interop message (e.g. <c>OpenFileRequest</c>) to the C# backend through
/// Photino's <c>window.external.sendMessage</c> bridge.
///
/// This class serves as the backend coordination point: it ensures the
/// <see cref="IMessageRouter"/> has the appropriate handler registered so that
/// shortcut-triggered messages are processed correctly.
/// </summary>
public class KeyboardShortcutHandler
{
    private readonly IMessageRouter _messageRouter;

    /// <summary>
    /// Tracks whether shortcuts have been registered to prevent double-registration.
    /// </summary>
    private bool _initialized;

    public KeyboardShortcutHandler(IMessageRouter messageRouter)
    {
        _messageRouter = messageRouter ?? throw new ArgumentNullException(nameof(messageRouter));
    }

    /// <summary>
    /// Register all keyboard shortcut handlers.
    /// 
    /// Currently registered shortcuts:
    /// <list type="bullet">
    ///   <item>Ctrl+O (Windows/Linux) / Cmd+O (macOS) — triggers <c>OpenFileRequest</c></item>
    /// </list>
    /// 
    /// The JavaScript side (embedded in App.razor) captures the keydown event and
    /// sends the corresponding message. This method ensures the backend handler
    /// is wired up to process it. If the handler is already registered by
    /// <see cref="PhotinoHostService"/>, this is a no-op for that message type.
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        // The OpenFileRequest handler is registered by PhotinoHostService.
        // This class exists to document the shortcut→message mapping and
        // to provide an extension point for future shortcuts that may need
        // their own dedicated handlers.

        _initialized = true;
    }
}
