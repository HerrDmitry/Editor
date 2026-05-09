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
    private readonly IViewportService? _viewportService;

    /// <summary>
    /// Lock for all _refreshCts and _scanCts dispose/create operations.
    /// Prevents ObjectDisposedException when concurrent threads access CTS.
    /// (Fix 9.1: CTS synchronization)
    /// </summary>
    private readonly object _ctsLock = new();

    /// <summary>
    /// Lock for _debounceTimer dispose/create operations.
    /// Uses replace-then-dispose pattern to prevent timer callback race.
    /// (Fix 9.4: Timer synchronization)
    /// </summary>
    private readonly object _timerLock = new();

    /// <summary>
    /// Cancellation token source for the current file scan operation.
    /// Cancelled when a new file is opened while a scan is in progress.
    /// Guarded by _ctsLock.
    /// </summary>
    private CancellationTokenSource? _scanCts;

    /// <summary>
    /// Cancellation token source for the current refresh operation.
    /// Separate from _scanCts so file-open and refresh don't interfere.
    /// Cancelled on shutdown or when opening a different file.
    /// Guarded by _ctsLock.
    /// </summary>
    private CancellationTokenSource? _refreshCts;

    /// <summary>
    /// Master shutdown token — cancelled in Shutdown() to stop all background work.
    /// </summary>
    private readonly CancellationTokenSource _shutdownCts = new();

    /// <summary>
    /// Path of the currently open file, used by HandleRequestLinesAsync.
    /// Marked volatile to ensure visibility across threads (Fix 9.2).
    /// </summary>
    private volatile string? _currentFilePath;

    /// <summary>
    /// Watches the currently open file for external modifications.
    /// </summary>
    private FileSystemWatcher? _fileWatcher;

    /// <summary>
    /// Debounce timer — coalesces rapid file change events into a single refresh.
    /// Guarded by _timerLock.
    /// </summary>
    private System.Threading.Timer? _debounceTimer;

    /// <summary>
    /// Debounce window in milliseconds for file change events.
    /// </summary>
    private const int DebounceMs = 500;

    /// <summary>
    /// Guard flag — 1 while a refresh cycle is executing, 0 otherwise.
    /// Prevents same-file change notifications from cancelling in-progress refresh.
    /// Uses Interlocked for thread-safe access.
    /// </summary>
    private int _refreshInProgress;

    /// <summary>
    /// Set to true when a change notification arrives while _refreshInProgress is true.
    /// After the current refresh completes, a new debounce cycle starts.
    /// </summary>
    private volatile bool _pendingRefresh;

    /// <summary>
    /// Stores the last viewport request parameters for replay after scan completion.
    /// Written in HandleRequestViewportAsync, read in OpenFileByPathAsync after large-file scan.
    /// Marked volatile for cross-thread visibility.
    /// </summary>
    private volatile RequestViewport? _lastViewportRequest;

    /// <summary>
    /// Stored delegate for OnStaleFileDetected subscription so we can unsubscribe in Shutdown.
    /// (Fix 9.3: Event unsubscription)
    /// </summary>
    private Action<string>? _staleFileHandler;

    public PhotinoHostService(PhotinoBlazorApp app, IMessageRouter messageRouter, IFileService fileService, IViewportService? viewportService = null)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _messageRouter = messageRouter ?? throw new ArgumentNullException(nameof(messageRouter));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _viewportService = viewportService;

        ConfigureWindow();
        RegisterMessageHandlers();
    }

    /// <summary>
    /// Internal constructor for unit testing — bypasses PhotinoBlazorApp window setup.
    /// </summary>
    internal PhotinoHostService(IMessageRouter messageRouter, IFileService fileService, IViewportService? viewportService = null)
    {
        _app = null!;
        _messageRouter = messageRouter ?? throw new ArgumentNullException(nameof(messageRouter));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _viewportService = viewportService;

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
        // Signal all background work to stop
        _shutdownCts.Cancel();

        lock (_ctsLock)
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;
        }

        StopWatching();

        // Unsubscribe from stale file detection event (Fix 9.3)
        if (_fileService is FileService fs && _staleFileHandler is not null)
        {
            fs.OnStaleFileDetected -= _staleFileHandler;
            _staleFileHandler = null;
        }

        _shutdownCts.Dispose();
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
        _messageRouter.RegisterHandler<RequestLineChunk>(HandleRequestLineChunkAsync);
        _messageRouter.RegisterHandler<RequestViewport>(HandleRequestViewportAsync);

        // Subscribe to stale file detection — route through debounced refresh.
        // Store delegate reference so Shutdown can unsubscribe (Fix 9.3).
        if (_fileService is FileService fs)
        {
            _staleFileHandler = (path) =>
            {
                if (path == _currentFilePath)
                {
                    OnFileChanged(this, new FileSystemEventArgs(WatcherChangeTypes.Changed,
                        Path.GetDirectoryName(path)!, Path.GetFileName(path)));
                }
            };
            fs.OnStaleFileDetected += _staleFileHandler;
        }
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
            // Clear path at start — prevents stale path if scan fails (Fix 9.5)
            _currentFilePath = null;

            // Cancel any existing scan or refresh in progress (Fix 9.1: under lock)
            lock (_ctsLock)
            {
                _scanCts?.Cancel();
                _scanCts?.Dispose();
                _refreshCts?.Cancel();
                _refreshCts?.Dispose();
                _refreshCts = null;
            }
            _pendingRefresh = false;

            // Create new CancellationTokenSource for this scan
            lock (_ctsLock)
            {
                _scanCts = new CancellationTokenSource();
            }

            // Progress<T> callback runs on thread pool. SendToUIAsync is thread-safe
            // (stateless, delegates to Photino native SendWebMessage). (Fix 9.6)
            var progress = new Progress<FileLoadProgress>(p =>
            {
                _ = _messageRouter.SendToUIAsync(new FileLoadProgressMessage
                {
                    FileName = p.FileName,
                    Percent = p.Percent,
                    FileSizeBytes = p.FileSizeBytes
                });
            });

            // Track whether partial metadata was emitted (large file path).
            // Used to decide whether to push viewport after scan completion.
            bool partialWasEmitted = false;

            // Partial metadata callback — sends partial FileOpenedResponse so UI can display content early.
            // Set path early so HandleRequestLinesAsync can serve lines during scan.
            // The line index cache is populated before this callback fires, so reads work.
            // If scan later fails, catch blocks will clear _currentFilePath.
            Action<FileOpenMetadata> onPartialMetadata = (partialMeta) =>
            {
                partialWasEmitted = true;
                _currentFilePath = filePath;
                _ = _messageRouter.SendToUIAsync(new FileOpenedResponse
                {
                    FileName = partialMeta.FileName,
                    TotalLines = partialMeta.TotalLines,
                    FileSizeBytes = partialMeta.FileSizeBytes,
                    Encoding = partialMeta.Encoding,
                    IsPartial = true,
                    MaxLineLength = partialMeta.MaxLineLength
                });
            };

            CancellationToken scanToken;
            lock (_ctsLock)
            {
                scanToken = _scanCts?.Token ?? CancellationToken.None;
            }

            // Scan the file to build line offset index and get metadata
            var metadata = await _fileService.OpenFileAsync(filePath, onPartialMetadata, progress, scanToken);

            // Store current file path only on success (Fix 9.5)
            _currentFilePath = filePath;

            // Start watching for external modifications
            StartWatching(filePath);

            // Send final metadata to the UI (scan complete)
            await _messageRouter.SendToUIAsync(new FileOpenedResponse
            {
                FileName = metadata.FileName,
                TotalLines = metadata.TotalLines,
                FileSizeBytes = metadata.FileSizeBytes,
                Encoding = metadata.Encoding,
                IsPartial = false,
                MaxLineLength = metadata.MaxLineLength
            });

            // Push viewport content after scan completion for large files.
            // Small files (no partialWasEmitted) skip this entirely.
            if (partialWasEmitted && _viewportService is not null)
            {
                if (!scanToken.IsCancellationRequested)
                {
                    try
                    {
                        var vp = _lastViewportRequest;
                        var startLine = vp?.StartLine ?? 0;
                        var lineCount = vp?.LineCount ?? 100;
                        var startColumn = vp?.StartColumn ?? 0;
                        var columnCount = vp?.ColumnCount ?? 200;
                        var wrapMode = vp?.WrapMode ?? false;
                        var viewportColumns = vp?.ViewportColumns ?? 200;

                        var result = await _viewportService.GetViewportAsync(
                            filePath, startLine, lineCount, startColumn, columnCount, wrapMode, viewportColumns, scanToken);

                        await _messageRouter.SendToUIAsync(new ViewportResponse
                        {
                            Lines = result.Lines,
                            StartLine = result.StartLine,
                            StartColumn = result.StartColumn,
                            TotalPhysicalLines = result.TotalPhysicalLines,
                            LineLengths = result.LineLengths,
                            MaxLineLength = result.MaxLineLength,
                            TotalVirtualLines = result.TotalVirtualLines,
                            Truncated = result.Truncated
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        // Scan was cancelled, skip viewport push
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARN] Failed to push viewport after scan complete: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled due to a new file being opened — log and do not send error to UI (Requirement 9.3)
            _currentFilePath = null;
            Console.Error.WriteLine("[INFO] File scan cancelled due to new file open request.");
        }
        catch (FileNotFoundException ex)
        {
            _currentFilePath = null;
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
            _currentFilePath = null;
            Console.Error.WriteLine($"[ERROR] Permission denied\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.PERMISSION_DENIED.ToString(),
                Message = "You do not have permission to read this file."
            });
        }
        catch (System.Text.Json.JsonException ex)
        {
            _currentFilePath = null;
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
            _currentFilePath = null;
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
                TotalLines = result.TotalLines,
                LineLengths = result.LineLengths
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

    /// <summary>
    /// Handle a <see cref="RequestLineChunk"/> from the React frontend:
    /// read the requested chunk of a large line and send it back.
    /// </summary>
    private async Task HandleRequestLineChunkAsync(RequestLineChunk request)
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

            var result = await _fileService.ReadLineChunkAsync(
                _currentFilePath, request.LineNumber, request.StartColumn, request.ColumnCount);

            await _messageRouter.SendToUIAsync(new LineChunkResponse
            {
                LineNumber = result.LineNumber,
                StartColumn = result.StartColumn,
                Text = result.Text,
                TotalLineChars = result.TotalLineChars,
                HasMore = result.HasMore
            });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.Error.WriteLine($"[ERROR] Invalid line number in chunk request: {ex}\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.UNKNOWN_ERROR.ToString(),
                Message = "Invalid line number specified.",
                Details = ex.Message
            });
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"[ERROR] File not found during chunk read: {ex.FileName}\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.FILE_NOT_FOUND.ToString(),
                Message = "The file could not be found. It may have been moved or deleted.",
                Details = ex.FileName
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Error reading line chunk\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.UNKNOWN_ERROR.ToString(),
                Message = "An unexpected error occurred while reading the line chunk.",
                Details = ex.Message
            });
        }
    }

    /// <summary>
    /// Handle a <see cref="RequestViewport"/> from the React frontend:
    /// read the requested viewport slice and send it back.
    /// </summary>
    private async Task HandleRequestViewportAsync(RequestViewport request)
    {
        _lastViewportRequest = request;

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

            if (_viewportService is null)
            {
                await _messageRouter.SendToUIAsync(new ErrorResponse
                {
                    ErrorCode = Models.ErrorCode.UNKNOWN_ERROR.ToString(),
                    Message = "Viewport service is not available."
                });
                return;
            }

            var result = await _viewportService.GetViewportAsync(
                _currentFilePath,
                request.StartLine,
                request.LineCount,
                request.StartColumn,
                request.ColumnCount,
                request.WrapMode,
                request.ViewportColumns);

            await _messageRouter.SendToUIAsync(new ViewportResponse
            {
                Lines = result.Lines,
                StartLine = result.StartLine,
                StartColumn = result.StartColumn,
                TotalPhysicalLines = result.TotalPhysicalLines,
                LineLengths = result.LineLengths,
                MaxLineLength = result.MaxLineLength,
                TotalVirtualLines = result.TotalVirtualLines,
                Truncated = result.Truncated
            });
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"[ERROR] File not found during viewport read: {ex.FileName}\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.FILE_NOT_FOUND.ToString(),
                Message = "The file could not be found. It may have been moved or deleted.",
                Details = ex.FileName
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Error reading viewport\n{ex}");
            await _messageRouter.SendToUIAsync(new ErrorResponse
            {
                ErrorCode = Models.ErrorCode.UNKNOWN_ERROR.ToString(),
                Message = "An unexpected error occurred while reading the viewport.",
                Details = ex.Message
            });
        }
    }

    /// <summary>
    /// Start watching the given file path. Disposes previous watcher if any.
    /// </summary>
    private void StartWatching(string filePath)
    {
        StopWatching();
        var dir = Path.GetDirectoryName(filePath)!;
        var name = Path.GetFileName(filePath);
        try
        {
            _fileWatcher = new FileSystemWatcher(dir, name)
            {
                // Only LastWrite — Size unsupported on some platforms (Fix 9.7)
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Error += OnWatcherError;
        }
        catch (Exception ex)
        {
            // Watcher creation can fail for non-existent directories, network paths, etc.
            // Stale detection in ReadLinesAsync serves as fallback (Requirement 1.5).
            Console.Error.WriteLine($"[WARN] Could not start file watcher: {ex.Message}");
            _fileWatcher = null;
        }
    }

    /// <summary>
    /// Stop watching and dispose watcher resources.
    /// </summary>
    private void StopWatching()
    {
        if (_fileWatcher is not null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Changed -= OnFileChanged;
            _fileWatcher.Error -= OnWatcherError;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }

        // Dispose timer under lock (Fix 9.4)
        System.Threading.Timer? oldTimer;
        lock (_timerLock)
        {
            oldTimer = _debounceTimer;
            _debounceTimer = null;
        }
        oldTimer?.Dispose();
    }

    /// <summary>
    /// FSW changed handler — reset debounce timer.
    /// Uses replace-then-dispose pattern under lock (Fix 9.4).
    /// </summary>
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_shutdownCts.IsCancellationRequested) return;

        System.Threading.Timer? oldTimer;
        lock (_timerLock)
        {
            oldTimer = _debounceTimer;
            _debounceTimer = new System.Threading.Timer(
                _ => _ = OnDebouncedFileChange(),
                null,
                DebounceMs,
                Timeout.Infinite);
        }
        oldTimer?.Dispose();
    }

    /// <summary>
    /// Debounce elapsed — trigger refresh cycle.
    /// Uses Interlocked guard to prevent overlapping refreshes.
    /// Same-file change notifications do NOT cancel an in-progress refresh;
    /// instead _pendingRefresh is set and a new cycle starts after completion.
    /// Only OpenFileByPathAsync (different file) cancels via _refreshCts.
    /// </summary>
    private async Task OnDebouncedFileChange()
    {
        if (_shutdownCts.IsCancellationRequested) return;
        if (string.IsNullOrEmpty(_currentFilePath)) return;

        // If refresh already running, mark pending and return.
        if (Interlocked.CompareExchange(ref _refreshInProgress, 1, 0) != 0)
        {
            _pendingRefresh = true;
            return;
        }

        try
        {
            CancellationToken refreshToken;

            // Create a dedicated CTS for this refresh, linked to shutdown (Fix 9.1: under lock).
            lock (_ctsLock)
            {
                _refreshCts?.Dispose();
                _refreshCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
                refreshToken = _refreshCts.Token;
            }

            var metadata = await _fileService.RefreshFileAsync(
                _currentFilePath, progress: null, refreshToken);

            if (!_shutdownCts.IsCancellationRequested)
            {
                await _messageRouter.SendToUIAsync(new FileOpenedResponse
                {
                    FileName = metadata.FileName,
                    TotalLines = metadata.TotalLines,
                    FileSizeBytes = metadata.FileSizeBytes,
                    Encoding = metadata.Encoding,
                    IsPartial = false,
                    IsRefresh = true,
                    MaxLineLength = metadata.MaxLineLength
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled — shutdown or new file opened. Silent.
        }
        catch (FileNotFoundException)
        {
            if (!_shutdownCts.IsCancellationRequested)
            {
                await _messageRouter.SendToUIAsync(new ErrorResponse
                {
                    ErrorCode = Models.ErrorCode.FILE_NOT_FOUND.ToString(),
                    Message = "The file has been deleted or moved.",
                    Details = _currentFilePath
                });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Refresh failed: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshInProgress, 0);

            // If a change arrived while we were refreshing, start a new debounce cycle (Fix 9.4: under lock).
            if (_pendingRefresh && !_shutdownCts.IsCancellationRequested)
            {
                _pendingRefresh = false;
                System.Threading.Timer? oldTimer;
                lock (_timerLock)
                {
                    oldTimer = _debounceTimer;
                    _debounceTimer = new System.Threading.Timer(
                        _ => _ = OnDebouncedFileChange(),
                        null,
                        DebounceMs,
                        Timeout.Infinite);
                }
                oldTimer?.Dispose();
            }
        }
    }

    /// <summary>
    /// Watcher error — could indicate network drive disconnect, etc.
    /// Fall back to stale detection in ReadLinesAsync as fallback.
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Console.Error.WriteLine($"[WARN] FileSystemWatcher error: {e.GetException()}");
    }
}
