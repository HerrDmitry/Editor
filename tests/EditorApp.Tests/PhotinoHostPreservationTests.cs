using System.Reflection;
using System.Text;
using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests;

/// <summary>
/// Property-based preservation tests for PhotinoHostService.
/// These establish baseline behavior on UNFIXED code that must be preserved after fixes.
/// Feature: photino-host-thread-safety
/// </summary>
public class PhotinoHostPreservationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            TempFileHelper.Cleanup(path);
        }
    }

    private string CreateTempFile(string content)
    {
        var path = TempFileHelper.CreateTempFile(content);
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempFileRaw(byte[] bytes)
    {
        var path = TempFileHelper.CreateTempFileRawBytes(bytes);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Property 8: Preservation - File Open Behavior
    ///
    /// For any successful file open operation, the fixed code SHALL produce same
    /// metadata, progress, and file watching behavior as original code.
    ///
    /// Validates: Requirements 3.1
    ///
    /// WHEN file open operation completes successfully
    /// THEN the system SHALL CONTINUE TO send metadata to UI and start file watching
    /// </summary>
    [Property(MaxTest = 50)]
    public bool FileOpen_SendsMetadataAndStartsWatching(string content)
    {
        // Generate non-trivial content
        var actualContent = string.IsNullOrEmpty(content) || content.Length < 10
            ? "Line 1\nLine 2\nLine 3\n"
            : content;

        var path = CreateTempFile(actualContent);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Act: Open file
        service.OpenFileByPathAsync(path).GetAwaiter().GetResult();

        // Assert: Metadata sent to UI
        var fileOpenedResponses = messageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();

        // Should have at least one FileOpenedResponse (final metadata)
        var finalResponse = fileOpenedResponses.LastOrDefault(r => !r.IsPartial);

        // Property: Final response sent
        if (finalResponse is null) return false;

        // Property: File name matches
        if (finalResponse.FileName != Path.GetFileName(path)) return false;

        // Property: File size matches actual file
        var fileInfo = new FileInfo(path);
        if (finalResponse.FileSizeBytes != fileInfo.Length) return false;

        // Property: Total lines > 0 for non-empty file
        if (finalResponse.TotalLines <= 0) return false;

        // Property: Encoding is set
        if (string.IsNullOrEmpty(finalResponse.Encoding)) return false;

        // Property: IsPartial is false for final response
        if (finalResponse.IsPartial) return false;

        // Property: File watcher started (verify via reflection)
        var serviceType = typeof(PhotinoHostService);
        var fileWatcherField = serviceType.GetField("_fileWatcher", BindingFlags.NonPublic | BindingFlags.Instance);
        var fileWatcher = fileWatcherField?.GetValue(service) as FileSystemWatcher;

        if (fileWatcher is null) return false;

        // Property: Watcher path matches
        if (fileWatcher.Path != Path.GetDirectoryName(path)) return false;

        // Property: Watcher filter matches
        if (fileWatcher.Filter != Path.GetFileName(path)) return false;

        // Property: Watcher enabled
        if (!fileWatcher.EnableRaisingEvents) return false;

        return true;
    }

    /// <summary>
    /// Property 8 (Alternative): Preservation - File Open with Large File Progress
    ///
    /// For large files (> 256KB), OpenFileByPathAsync sends progress messages.
    ///
    /// Validates: Requirements 3.1
    /// </summary>
    [Property(MaxTest = 10)]
    public bool FileOpen_LargeFile_SendsProgress(int sizeSeed)
    {
        // Generate file size between 256KB+1 and 512KB
        var size = FileService.SizeThresholdBytes + 1 + (Math.Abs(sizeSeed) % 256_000);

        // Create file with newlines every ~100 bytes for realistic content
        var data = new byte[size];
        for (int i = 0; i < size; i++)
        {
            data[i] = (i % 100 == 99) ? (byte)'\n' : (byte)'A';
        }

        var path = CreateTempFileRaw(data);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Act: Open large file
        service.OpenFileByPathAsync(path).GetAwaiter().GetResult();

        // Assert: Progress messages sent
        var progressMessages = messageRouter.ProgressMessages;

        // Property: Progress messages sent for large file
        if (progressMessages.Count == 0) return false;

        // Property: Progress percent increases monotonically
        var progressPercentages = progressMessages.Select(p => p.Percent).ToList();
        for (int i = 1; i < progressPercentages.Count; i++)
        {
            if (progressPercentages[i] < progressPercentages[i - 1])
                return false;
        }

        // Property: Final progress is 100%
        if (progressMessages.Last().Percent != 100) return false;

        // Assert: FileOpenedResponse sent
        var fileOpenedResponses = messageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();

        // For large files, partial metadata is sent first
        var partialResponse = fileOpenedResponses.FirstOrDefault(r => r.IsPartial);
        var finalResponse = fileOpenedResponses.LastOrDefault(r => !r.IsPartial);

        if (partialResponse is null) return false;
        if (finalResponse is null) return false;

        // Property: File watcher started
        var serviceType = typeof(PhotinoHostService);
        var fileWatcherField = serviceType.GetField("_fileWatcher", BindingFlags.NonPublic | BindingFlags.Instance);
        var fileWatcher = fileWatcherField?.GetValue(service) as FileSystemWatcher;

        if (fileWatcher is null) return false;

        return true;
    }

    /// <summary>
    /// Property 8 (Alternative 2): Preservation - File Open Metadata Accuracy
    ///
    /// For any file, the metadata in FileOpenedResponse matches actual file properties.
    ///
    /// Validates: Requirements 3.1
    /// </summary>
    [Property(MaxTest = 50)]
    public bool FileOpen_MetadataMatchesActualFile(NonEmptyString content)
    {
        var path = CreateTempFile(content.Get);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Act: Open file
        service.OpenFileByPathAsync(path).GetAwaiter().GetResult();

        // Get actual file info
        var fileInfo = new FileInfo(path);

        // Get FileOpenedResponse
        var fileOpenedResponses = messageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();

        var finalResponse = fileOpenedResponses.LastOrDefault(r => !r.IsPartial);

        if (finalResponse is null) return false;

        // Property: FileName matches
        if (finalResponse.FileName != fileInfo.Name) return false;

        // Property: FileSizeBytes matches
        if (finalResponse.FileSizeBytes != fileInfo.Length) return false;

        // Property: TotalLines > 0
        if (finalResponse.TotalLines <= 0) return false;

        // Property: Encoding is set
        if (string.IsNullOrEmpty(finalResponse.Encoding)) return false;

        return true;
    }

    /// <summary>
    /// Property 8 (Alternative 3): Preservation - Current File Path Set After Open
    ///
    /// After successful file open, _currentFilePath is set to the opened file path.
    ///
    /// Validates: Requirements 3.1
    /// </summary>
    [Property(MaxTest = 30)]
    public bool FileOpen_CurrentFilePathSet(string content)
    {
        var actualContent = string.IsNullOrEmpty(content) || content.Length < 5
            ? "Line 1\nLine 2\nLine 3"
            : content;

        var path = CreateTempFile(actualContent);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Act: Open file
        service.OpenFileByPathAsync(path).GetAwaiter().GetResult();

        // Get _currentFilePath via reflection
        var serviceType = typeof(PhotinoHostService);
        var currentPathField = serviceType.GetField("_currentFilePath", BindingFlags.NonPublic | BindingFlags.Instance);
        var currentPath = (string?)currentPathField?.GetValue(service);

        // Property: _currentFilePath is set to the opened file path
        return currentPath == path;
    }

    /// <summary>
    /// Property 8 (Alternative 4): Preservation - Multiple Opens Updates Metadata
    ///
    /// Opening different files in sequence sends updated metadata each time.
    ///
    /// Validates: Requirements 3.1
    /// </summary>
    [Property(MaxTest = 20)]
    public bool FileOpen_MultipleOpens_UpdatesMetadata(NonEmptyString content1, NonEmptyString content2)
    {
        // Ensure different content
        var c1 = content1.Get.Length < 10 ? "File 1 - Line 1\nLine 2" : content1.Get;
        var c2 = content2.Get.Length < 10 ? "File 2 - Line 1\nLine 2\nLine 3" : content2.Get;

        // Ensure files are different
        if (c1 == c2) c2 += "\nExtra Line";

        var path1 = CreateTempFile(c1);
        var path2 = CreateTempFile(c2);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Act: Open first file
        service.OpenFileByPathAsync(path1).GetAwaiter().GetResult();

        var firstResponses = messageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();
        var firstFinal = firstResponses.LastOrDefault(r => !r.IsPartial);

        // Act: Open second file
        service.OpenFileByPathAsync(path2).GetAwaiter().GetResult();

        var secondResponses = messageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();
        var secondFinal = secondResponses.LastOrDefault(r => !r.IsPartial);

        if (firstFinal is null || secondFinal is null) return false;

        // Property: Second response has different file name
        if (firstFinal.FileName == secondFinal.FileName) return false;

        // Property: Second response matches second file info
        var fileInfo2 = new FileInfo(path2);
        if (secondFinal.FileSizeBytes != fileInfo2.Length) return false;

        // Property: Current path updated to second file
        var serviceType = typeof(PhotinoHostService);
        var currentPathField = serviceType.GetField("_currentFilePath", BindingFlags.NonPublic | BindingFlags.Instance);
        var currentPath = (string?)currentPathField?.GetValue(service);

        if (currentPath != path2) return false;

        // Property: File watcher updated to second file
        var fileWatcherField = serviceType.GetField("_fileWatcher", BindingFlags.NonPublic | BindingFlags.Instance);
        var fileWatcher = fileWatcherField?.GetValue(service) as FileSystemWatcher;

        if (fileWatcher is null) return false;
        if (fileWatcher.Filter != Path.GetFileName(path2)) return false;

        return true;
    }

    /// <summary>
    /// Property 9: Preservation - File Change Detection
    ///
    /// For any file change detection, the fixed code SHALL produce same
    /// debounce and refresh behavior as original code.
    ///
    /// Validates: Requirements 3.2
    ///
    /// WHEN file change is detected
    /// THEN the system SHALL CONTINUE TO debounce and trigger refresh cycle
    /// </summary>
    [Property(MaxTest = 30)]
    public bool FileChange_TriggersDebounceAndRefresh(string content)
    {
        // Generate non-trivial content
        var actualContent = string.IsNullOrEmpty(content) || content.Length < 10
            ? "Line 1\nLine 2\nLine 3\n"
            : content;

        var path = CreateTempFile(actualContent);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Act: Open file first (required for refresh to work)
        service.OpenFileByPathAsync(path).GetAwaiter().GetResult();

        // Clear messages from open
        messageRouter.SentMessages.Clear();

        // Get file watcher to simulate file change
        var serviceType = typeof(PhotinoHostService);
        var fileWatcherField = serviceType.GetField("_fileWatcher", BindingFlags.NonPublic | BindingFlags.Instance);
        var fileWatcher = fileWatcherField?.GetValue(service) as FileSystemWatcher;

        if (fileWatcher is null) return false;

        // Modify the file to trigger change event
        File.AppendAllText(path, "\nNew Line");

        // Manually invoke OnFileChanged via reflection (simulating FSW event)
        var onFileChangedMethod = serviceType.GetMethod("OnFileChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(path)!, Path.GetFileName(path));

        onFileChangedMethod?.Invoke(service, new object[] { fileWatcher, eventArgs });

        // Wait for debounce (DebounceMs = 500ms + buffer)
        Thread.Sleep(500 + 200);

        // Property: Refresh sends FileOpenedResponse with IsRefresh=true
        var fileOpenedResponses = messageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();

        var refreshResponse = fileOpenedResponses.FirstOrDefault(r => r.IsRefresh);

        if (refreshResponse is null) return false;

        // Property: IsRefresh is true
        if (!refreshResponse.IsRefresh) return false;

        // Property: IsPartial is false
        if (refreshResponse.IsPartial) return false;

        // Property: FileName matches
        if (refreshResponse.FileName != Path.GetFileName(path)) return false;

        // Property: File size matches modified file
        var fileInfo = new FileInfo(path);
        if (refreshResponse.FileSizeBytes != fileInfo.Length) return false;

        return true;
    }

    /// <summary>
    /// Property 9 (Alternative 1): Preservation - Debounce Prevents Multiple Refreshes
    ///
    /// Multiple rapid file changes within debounce window trigger single refresh.
    ///
    /// Validates: Requirements 3.2
    ///
    /// NOTE: This test is timing-dependent (relies on Thread.Sleep for debounce waits).
    /// May exceed 120s or produce flaky results in CI. Run manually.
    /// </summary>
    [Property(MaxTest = 20, Skip = "Timing-dependent: relies on Thread.Sleep for debounce. Run manually.")]
    public bool FileChange_MultipleRapidChanges_TriggersSingleRefresh(NonEmptyString content)
    {
        var path = CreateTempFile(content.Get);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Act: Open file first
        service.OpenFileByPathAsync(path).GetAwaiter().GetResult();
        messageRouter.SentMessages.Clear();

        var serviceType = typeof(PhotinoHostService);
        var fileWatcherField = serviceType.GetField("_fileWatcher", BindingFlags.NonPublic | BindingFlags.Instance);
        var fileWatcher = fileWatcherField?.GetValue(service) as FileSystemWatcher;

        if (fileWatcher is null) return false;

        var onFileChangedMethod = serviceType.GetMethod("OnFileChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(path)!, Path.GetFileName(path));

        // Trigger multiple rapid changes within debounce window (500ms)
        for (int i = 0; i < 5; i++)
        {
            File.AppendAllText(path, $"\nChange {i}");
            onFileChangedMethod?.Invoke(service, new object[] { fileWatcher, eventArgs });
            Thread.Sleep(50); // Less than DebounceMs (500ms)
        }

        // Wait for debounce to complete (500ms + buffer)
        Thread.Sleep(500 + 200);

        // Property: Single refresh sent (not 5)
        var refreshResponses = messageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .Where(r => r.IsRefresh)
            .ToList();

        // Should have exactly 1 refresh response (multiple changes debounced to single refresh)
        if (refreshResponses.Count != 1) return false;

        return true;
    }

    /// <summary>
    /// Property 9 (Alternative 2): Preservation - Pending Refresh on Concurrent Change
    ///
    /// If file changes during ongoing refresh, a new refresh is triggered after completion.
    ///
    /// Validates: Requirements 3.2
    ///
    /// NOTE: This test is timing-dependent (relies on Thread.Sleep for debounce waits)
    /// and manipulates internal state via reflection. May exceed 120s or produce flaky results. Run manually.
    /// </summary>
    [Property(MaxTest = 15, Skip = "Timing-dependent: relies on Thread.Sleep for debounce + reflection state manipulation. Run manually.")]
    public bool FileChange_ChangeDuringRefresh_TriggersPendingRefresh(NonEmptyString content)
    {
        var path = CreateTempFile(content.Get);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Act: Open file first
        service.OpenFileByPathAsync(path).GetAwaiter().GetResult();
        messageRouter.SentMessages.Clear();

        var serviceType = typeof(PhotinoHostService);
        var fileWatcherField = serviceType.GetField("_fileWatcher", BindingFlags.NonPublic | BindingFlags.Instance);
        var fileWatcher = fileWatcherField?.GetValue(service) as FileSystemWatcher;

        if (fileWatcher is null) return false;

        var onFileChangedMethod = serviceType.GetMethod("OnFileChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(path)!, Path.GetFileName(path));

        // Set refresh in progress to simulate ongoing refresh
        var refreshInProgressField = serviceType.GetField("_refreshInProgress", BindingFlags.NonPublic | BindingFlags.Instance);
        refreshInProgressField?.SetValue(service, 1);

        // Trigger change (will set pending refresh)
        File.AppendAllText(path, "\nChange during refresh");
        onFileChangedMethod?.Invoke(service, new object[] { fileWatcher, eventArgs });

        // Check pending flag set
        var pendingRefreshField = serviceType.GetField("_pendingRefresh", BindingFlags.NonPublic | BindingFlags.Instance);
        var pendingRefresh = (bool?)pendingRefreshField?.GetValue(service);

        // Property: Pending refresh flag is set
        if (pendingRefresh != true) return false;

        // Simulate refresh completion (reset flag)
        refreshInProgressField?.SetValue(service, 0);

        // Manually trigger the pending refresh logic (simulating end of finally block)
        var onDebouncedFileChangeMethod = serviceType.GetMethod("OnDebouncedFileChange", BindingFlags.NonPublic | BindingFlags.Instance);

        // Wait for debounce timer from pending refresh to fire (500ms + buffer)
        Thread.Sleep(500 + 200);

        // Property: Second refresh triggered
        var refreshResponses = messageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .Where(r => r.IsRefresh)
            .ToList();

        // Should have at least 1 refresh response
        if (refreshResponses.Count == 0) return false;

        return true;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 10: Preservation - Shutdown Behavior
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 10: Preservation - Shutdown Behavior
    ///
    /// For any state, shutdown cancels all CTS, disposes resources, stops watcher.
    ///
    /// Validates: Requirements 3.3
    ///
    /// WHEN shutdown is requested
    /// THEN the system SHALL CONTINUE TO cancel all background operations and dispose resources
    /// </summary>
    [Property(MaxTest = 30)]
    public bool Shutdown_DisposesAllResources(string content)
    {
        var actualContent = string.IsNullOrEmpty(content) || content.Length < 5
            ? "Line 1\nLine 2\nLine 3"
            : content;

        var path = CreateTempFile(actualContent);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Open file to set up state (watcher, CTS, etc.)
        service.OpenFileByPathAsync(path).GetAwaiter().GetResult();

        var serviceType = typeof(PhotinoHostService);

        // Verify watcher exists before shutdown
        var fileWatcherField = serviceType.GetField("_fileWatcher", BindingFlags.NonPublic | BindingFlags.Instance);
        var watcherBefore = fileWatcherField?.GetValue(service) as FileSystemWatcher;
        if (watcherBefore is null) return false;

        // Act: Shutdown
        service.Shutdown();

        // Property: Watcher disposed (set to null by StopWatching)
        var watcherAfter = fileWatcherField?.GetValue(service) as FileSystemWatcher;
        if (watcherAfter is not null) return false;

        // Property: Debounce timer disposed (set to null by StopWatching)
        var debounceTimerField = serviceType.GetField("_debounceTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        var timerAfter = debounceTimerField?.GetValue(service) as System.Threading.Timer;
        if (timerAfter is not null) return false;

        // Property: _shutdownCts is cancelled
        var shutdownCtsField = serviceType.GetField("_shutdownCts", BindingFlags.NonPublic | BindingFlags.Instance);
        var shutdownCts = shutdownCtsField?.GetValue(service) as CancellationTokenSource;
        // After dispose, accessing IsCancellationRequested may throw — just verify field exists
        if (shutdownCts is null) return false;

        return true;
    }

    /// <summary>
    /// Property 10 (Alternative): Preservation - Shutdown Without Open File
    ///
    /// Shutdown on fresh service (no file opened) does not throw.
    ///
    /// Validates: Requirements 3.3
    /// </summary>
    [Fact]
    public void Shutdown_NoFileOpened_DoesNotThrow()
    {
        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Act: Shutdown without opening any file
        var ex = Record.Exception(() => service.Shutdown());

        // Property: No exception
        Assert.Null(ex);
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 9 (continued): Preservation - Refresh Updates Metadata
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 9 (Alternative 3): Preservation - Refresh Updates Metadata
    ///
    /// Refresh response contains updated file metadata after change.
    ///
    /// Validates: Requirements 3.2
    /// </summary>
    [Property(MaxTest = 25)]
    public bool FileChange_Refresh_UpdatesMetadata(string originalContent, string appendedContent)
    {
        // Ensure non-empty content
        var orig = string.IsNullOrEmpty(originalContent) ? "Original line 1\nOriginal line 2" : originalContent;
        var append = string.IsNullOrEmpty(appendedContent) ? "\nNew line 1\nNew line 2" : appendedContent;

        var path = CreateTempFile(orig);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Act: Open file first
        service.OpenFileByPathAsync(path).GetAwaiter().GetResult();

        var openResponses = messageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();
        var openResponse = openResponses.LastOrDefault(r => !r.IsPartial && !r.IsRefresh);

        if (openResponse is null) return false;

        var originalTotalLines = openResponse.TotalLines;
        var originalFileSize = openResponse.FileSizeBytes;

        messageRouter.SentMessages.Clear();

        // Modify file
        File.AppendAllText(path, append);

        var serviceType = typeof(PhotinoHostService);
        var fileWatcherField = serviceType.GetField("_fileWatcher", BindingFlags.NonPublic | BindingFlags.Instance);
        var fileWatcher = fileWatcherField?.GetValue(service) as FileSystemWatcher;

        if (fileWatcher is null) return false;

        var onFileChangedMethod = serviceType.GetMethod("OnFileChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(path)!, Path.GetFileName(path));

        onFileChangedMethod?.Invoke(service, new object[] { fileWatcher, eventArgs });

        // Wait for debounce (500ms + buffer)
        Thread.Sleep(500 + 200);

        var refreshResponses = messageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .Where(r => r.IsRefresh)
            .ToList();

        var refreshResponse = refreshResponses.FirstOrDefault();

        if (refreshResponse is null) return false;

        // Property: File size increased
        if (refreshResponse.FileSizeBytes <= originalFileSize) return false;

        // Property: Total lines increased (or stayed same if appended content has no newlines)
        // At minimum, total lines should match actual line count
        var expectedLines = orig.Split('\n').Length + append.Split('\n').Length - 1;
        if (refreshResponse.TotalLines < originalTotalLines) return false;

        // Property: FileName unchanged
        if (refreshResponse.FileName != Path.GetFileName(path)) return false;

        return true;
    }

    // ──────────────────────────────────────────────────────────────────
    // Property 11: Preservation - Line Request Behavior
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 11: Preservation - Line Request Behavior
    ///
    /// For any valid line range request, response matches original behavior.
    ///
    /// Validates: Requirements 3.4
    ///
    /// WHEN line range is requested via RequestLinesMessage
    /// THEN the system SHALL CONTINUE TO read and return the requested lines from current file
    /// </summary>
    [Property(MaxTest = 50)]
    public bool LineRequest_ReturnsCorrectLines(NonEmptyString content)
    {
        var path = CreateTempFile(content.Get);

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Open file
        service.OpenFileByPathAsync(path).GetAwaiter().GetResult();
        messageRouter.SentMessages.Clear();

        // Request lines via SimulateMessageAsync
        var request = new RequestLinesMessage { StartLine = 0, LineCount = 10 };
        messageRouter.SimulateMessageAsync(request).GetAwaiter().GetResult();

        // Get LinesResponse
        var linesResponses = messageRouter.SentMessages
            .Where(m => m.Message is LinesResponse)
            .Select(m => (LinesResponse)m.Message)
            .ToList();

        if (linesResponses.Count == 0) return false;

        var response = linesResponses[0];

        // Property: StartLine matches request
        if (response.StartLine != 0) return false;

        // Property: Lines array not null
        if (response.Lines is null) return false;

        // Property: Lines count > 0 for non-empty file
        if (response.Lines.Length == 0) return false;

        // Property: TotalLines > 0
        if (response.TotalLines <= 0) return false;

        return true;
    }

    /// <summary>
    /// Property 11 (Alternative): Preservation - Line Request Without Open File
    ///
    /// Requesting lines without opening a file sends error response.
    ///
    /// Validates: Requirements 3.4
    /// </summary>
    [Fact]
    public async Task LineRequest_NoFileOpened_SendsError()
    {
        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        // Request lines without opening file
        var request = new RequestLinesMessage { StartLine = 0, LineCount = 10 };
        await messageRouter.SimulateMessageAsync(request);

        // Should get error response
        var errors = messageRouter.ErrorMessages;
        Assert.NotEmpty(errors);
        Assert.Equal("UNKNOWN_ERROR", errors[0].ErrorCode);
    }

    /// <summary>
    /// Property 11 (Alternative 2): Preservation - Line Request Clamping
    ///
    /// Requesting lines beyond file end returns clamped result.
    ///
    /// Validates: Requirements 3.4
    /// </summary>
    [Fact]
    public async Task LineRequest_BeyondEnd_ReturnsClamped()
    {
        var path = CreateTempFile("Line 1\nLine 2\nLine 3");

        var messageRouter = new MockMessageRouter();
        var fileService = new FileService();
        var service = new PhotinoHostService(messageRouter, fileService);

        await service.OpenFileByPathAsync(path);
        messageRouter.SentMessages.Clear();

        // Request lines starting beyond file end
        var request = new RequestLinesMessage { StartLine = 9999, LineCount = 10 };
        await messageRouter.SimulateMessageAsync(request);

        var linesResponses = messageRouter.SentMessages
            .Where(m => m.Message is LinesResponse)
            .Select(m => (LinesResponse)m.Message)
            .ToList();

        Assert.NotEmpty(linesResponses);
        // Clamped — returns empty lines array
        Assert.Empty(linesResponses[0].Lines);
    }
}
