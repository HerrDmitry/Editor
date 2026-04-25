using EditorApp.Models;
using Photino.Blazor;

namespace EditorApp.Services;

/// <summary>
/// Initializes and manages the Photino.Blazor window, wires up the message
/// router and file-open handler, and owns the application lifecycle.
/// </summary>
public class PhotinoHostService
{
    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 800;
    private const string DefaultTitle = "Editor";

    private readonly PhotinoBlazorApp _app;
    private readonly IMessageRouter _messageRouter;
    private readonly IFileService _fileService;

    public PhotinoHostService(PhotinoBlazorApp app, IMessageRouter messageRouter, IFileService fileService)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _messageRouter = messageRouter ?? throw new ArgumentNullException(nameof(messageRouter));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));

        ConfigureWindow();
        RegisterMessageHandlers();
    }

    /// <summary>
    /// Start the Photino application event loop. Blocks until the window is closed.
    /// </summary>
    public void Run()
    {
        _messageRouter.StartListening();
        _app.Run();
    }

    /// <summary>
    /// Clean up resources. Called when the application is shutting down.
    /// </summary>
    public void Shutdown()
    {
        // Photino.Blazor handles native resource cleanup when the window closes.
        // This method exists as an explicit lifecycle hook for future use
        // (e.g., saving state, flushing logs).
    }

    /// <summary>
    /// Configure the Photino window with default size, title, and behaviour.
    /// </summary>
    private void ConfigureWindow()
    {
        _app.MainWindow
            .SetTitle(DefaultTitle)
            .SetSize(DefaultWidth, DefaultHeight)
            .SetResizable(true)
            .Center();
    }

    /// <summary>
    /// Register backend message handlers with the <see cref="IMessageRouter"/>.
    /// </summary>
    private void RegisterMessageHandlers()
    {
        _messageRouter.RegisterHandler<OpenFileRequest>(HandleOpenFileRequestAsync);
    }

    /// <summary>
    /// Handle an <see cref="OpenFileRequest"/> from the React frontend:
    /// show the native file picker, read the file, and send the result back.
    /// </summary>
    private async Task HandleOpenFileRequestAsync(OpenFileRequest request)
    {
        try
        {
            // Show native file picker via Photino
            var selectedFiles = _app.MainWindow.ShowOpenFile();

            if (selectedFiles is null || selectedFiles.Length == 0 || string.IsNullOrEmpty(selectedFiles[0]))
            {
                // User cancelled the dialog — no action needed (Requirement 2.3).
                return;
            }

            var filePath = selectedFiles[0];

            // Validate file size and send warning if needed
            var fileInfo = new FileInfo(filePath);
            if (_fileService.ValidateFileSize(fileInfo.Length, out var warningMessage) && warningMessage is not null)
            {
                // File is between 10–50 MB — send a warning first, then continue loading.
                await _messageRouter.SendToUIAsync(new WarningResponse
                {
                    WarningCode = "LARGE_FILE",
                    Message = warningMessage,
                    FilePath = filePath,
                    FileSizeBytes = fileInfo.Length
                });
            }

            // Read the file
            var fileContent = await _fileService.ReadFileAsync(filePath);

            // Send loaded content to the UI
            await _messageRouter.SendToUIAsync(new FileLoadedResponse
            {
                Content = fileContent.Content,
                FilePath = fileContent.FilePath,
                FileName = fileContent.FileName,
                Metadata = new FileMetadataPayload
                {
                    FileSizeBytes = fileContent.Metadata.FileSizeBytes,
                    LineCount = fileContent.Metadata.LineCount,
                    Encoding = fileContent.Metadata.Encoding,
                    LastModified = fileContent.Metadata.LastModified.ToString("o")
                }
            });
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"[ERROR] File not found: {ex.FileName}\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.FILE_NOT_FOUND.ToString(),
                Message = "The selected file could not be found.",
                Details = ex.FileName
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"[ERROR] Permission denied\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.PERMISSION_DENIED.ToString(),
                Message = "You do not have permission to read this file."
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("exceeds maximum size"))
        {
            Console.Error.WriteLine($"[ERROR] File too large\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.FILE_TOO_LARGE.ToString(),
                Message = "This file is too large to open (maximum 50 MB)."
            });
        }
        catch (System.Text.Json.JsonException ex)
        {
            Console.Error.WriteLine($"[ERROR] JSON serialization failure\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.INTEROP_FAILURE.ToString(),
                Message = "An internal communication error occurred.",
                Details = ex.Message
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Unexpected error\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.UNKNOWN_ERROR.ToString(),
                Message = "An unexpected error occurred while reading the file.",
                Details = ex.Message
            });
        }
    }
}
