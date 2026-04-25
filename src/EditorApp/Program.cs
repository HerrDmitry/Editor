using EditorApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Photino.Blazor;

namespace EditorApp;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);

        // Serve wwwroot from embedded resources so no physical directory is needed.
        builder.Services.AddSingleton<IFileProvider>(
            new ManifestEmbeddedFileProvider(typeof(Program).Assembly, "wwwroot"));

        // Register the Blazor root component (App.razor renders the host HTML).
        builder.RootComponents.Add<App>("#app");

        var app = builder.Build();

        // Create the messaging adapter that bridges the real Photino window
        // to the IPhotinoWindowMessaging abstraction used by MessageRouter.
        var messagingAdapter = new PhotinoWindowMessagingAdapter(app);

        // Create core services
        var fileService = new FileService();
        var messageRouter = new MessageRouter(messagingAdapter);

        // Create the host service — this configures the window (1200×800,
        // centered, resizable, title "Editor") and registers message handlers.
        var hostService = new PhotinoHostService(app, messageRouter, fileService);

        // Initialize keyboard shortcut handler.
        var shortcutHandler = new KeyboardShortcutHandler(messageRouter);
        shortcutHandler.Initialize();

        // Global unhandled-exception handler
        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            app.MainWindow.ShowMessage("Fatal Exception", error.ExceptionObject.ToString());
        };

        // Start the application event loop (blocks until window is closed).
        hostService.Run();

        // Clean up on exit.
        hostService.Shutdown();
    }
}
