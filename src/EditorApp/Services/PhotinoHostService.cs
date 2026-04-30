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

    /// <summary>
    /// Cancellation token source for the current file scan operation.
    /// Cancelled when a new file is opened while a scan is in progress.
    /// </summary>
    private CancellationTokenSource? _scanCts;

    /// <summary>
    /// Path of the currently open file, used by HandleRequestLinesAsync.
    /// </summary>
    private string? _currentFilePath;

    public PhotinoHostService(PhotinoBlazorApp app, IMessageRouter messageRouter, IFileService fileService)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _messageRouter = messageRouter ?? throw new ArgumentNullException(nameof(messageRouter));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));

        ConfigureWindow();
        RegisterMessageHandlers();
    }

    /// <summary>
    /// Internal constructor for unit testing — bypasses PhotinoBlazorApp window setup.
    /// </summary>
    internal PhotinoHostService(IMessageRouter messageRouter, IFileService fileService)
    {
        _app = null!;
        _messageRouter = messageRouter ?? throw new ArgumentNullException(nameof(messageRouter));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));

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
        _messageRouter.RegisterHandler<RequestLinesMessage>(HandleRequestLinesAsync);
    }

    /// <summary>
    /// Handle an <see cref="OpenFileRequest"/> from the React frontend:
    /// show the native file picker, scan the file, and send metadata back.
    /// </summary>
    private async Task HandleOpenFileRequestAsync(OpenFileRequest request)
    {
        // Show native file picker via Photino
        var selectedFiles = _app.MainWindow.ShowOpenFile();

        if (selectedFiles is null || selectedFiles.Length == 0 || string.IsNullOrEmpty(selectedFiles[0]))
        {
            // User cancelled the dialog — no action needed (Requirement 2.3).
            return;
        }

        await OpenFileByPathAsync(selectedFiles[0]);
    }

    /// <summary>
    /// Core file-open logic: cancels any in-progress scan, creates progress reporter,
    /// scans the file, and sends metadata to the UI.
    /// Extracted from HandleOpenFileRequestAsync for testability.
    /// </summary>
    internal async Task OpenFileByPathAsync(string filePath)
    {
        try
        {
            // Cancel any existing scan in progress (Requirement 9.1)
            if (_scanCts is not null)
            {
                _scanCts.Cancel();
                _scanCts.Dispose();
            }

            // Create new CancellationTokenSource for this scan
            _scanCts = new CancellationTokenSource();

            // Create progress reporter that forwards to UI via MessageRouter
            var progress = new Progress<FileLoadProgress>(p =>
            {
                _ = _messageRouter.SendToUIAsync(new FileLoadProgressMessage
                {
                    FileName = p.FileName,
                    Percent = p.Percent,
                    FileSizeBytes = p.FileSizeBytes
                });
            });

            // Scan the file to build line offset index and get metadata
            var metadata = await _fileService.OpenFileAsync(filePath, progress, _scanCts.Token);

            // Store current file path for subsequent ReadLinesAsync calls
            _currentFilePath = filePath;

            // Send metadata to the UI (no file content)
            await _messageRouter.SendToUIAsync(new FileOpenedResponse
            {
                FileName = metadata.FileName,
                TotalLines = metadata.TotalLines,
                FileSizeBytes = metadata.FileSizeBytes,
                Encoding = metadata.Encoding
            });
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled due to a new file being opened — log and do not send error to UI (Requirement 9.3)
            Console.Error.WriteLine("[INFO] File scan cancelled due to new file open request.");
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
                Message = "An unexpected error occurred while opening the file.",
                Details = ex.Message
            });
        }
    }

    /// <summary>
    /// Handle a <see cref="RequestLinesMessage"/> from the React frontend:
    /// read the requested line range and send it back.
    /// </summary>
    private async Task HandleRequestLinesAsync(RequestLinesMessage request)
    {
        try
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                await _messageRouter.SendToUIAsync(new ErrorResponse
                {
                    ErrorCode = Models.ErrorCode.UNKNOWN_ERROR.ToString(),
                    Message = "No file is currently open."
                });
                return;
            }

            var result = await _fileService.ReadLinesAsync(_currentFilePath, request.StartLine, request.LineCount);

            await _messageRouter.SendToUIAsync(new LinesResponse
            {
                StartLine = result.StartLine,
                Lines = result.Lines,
                TotalLines = result.TotalLines
            });
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"[ERROR] File not found during line read: {ex.FileName}\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.FILE_NOT_FOUND.ToString(),
                Message = "The file could not be found. It may have been moved or deleted.",
                Details = ex.FileName
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Error reading lines\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.UNKNOWN_ERROR.ToString(),
                Message = "An unexpected error occurred while reading the file.",
                Details = ex.Message
            });
        }
    }
}
