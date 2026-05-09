using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests;

/// <summary>
/// Property 2: Preservation - Small File Opens and Non-Scan-Complete Flows Unchanged
///
/// These tests verify baseline behavior that MUST remain unchanged after the bugfix.
/// They PASS on unfixed code (confirming the behavior to preserve).
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
/// </summary>
public class ViewportPreservationPropertyTests
{
    /// <summary>
    /// Mock IFileService for small files (≤256 KB).
    /// Does NOT invoke onPartialMetadata — returns metadata directly.
    /// </summary>
    private class SmallFileMockFileService : IFileService
    {
        private readonly long _fileSizeBytes;
        private readonly int _totalLines;
        private readonly int _maxLineLength;

        public SmallFileMockFileService(long fileSizeBytes, int totalLines, int maxLineLength)
        {
            _fileSizeBytes = fileSizeBytes;
            _totalLines = totalLines;
            _maxLineLength = maxLineLength;
        }

        public Task<FileOpenResult> OpenFileDialogAsync()
            => Task.FromResult(new FileOpenResult(false, null, "Not implemented"));

        public Task<FileOpenMetadata> OpenFileAsync(
            string filePath,
            Action<FileOpenMetadata>? onPartialMetadata = null,
            IProgress<FileLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Small file: do NOT invoke onPartialMetadata
            // Return metadata directly
            return Task.FromResult(new FileOpenMetadata(
                filePath,
                Path.GetFileName(filePath),
                _totalLines,
                _fileSizeBytes,
                "UTF-8",
                _maxLineLength));
        }

        public Task<FileOpenMetadata> RefreshFileAsync(
            string filePath,
            IProgress<FileLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new FileOpenMetadata(filePath, Path.GetFileName(filePath), _totalLines, _fileSizeBytes, "UTF-8", _maxLineLength));

        public Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default)
            => Task.FromResult(new LinesResult(startLine, new[] { "line" }, _totalLines));

        public int GetTotalLines(string filePath) => _totalLines;
        public int GetMaxLineLength(string filePath) => _maxLineLength;
        public int GetLineCharLength(string filePath, int lineNumber) => 80;
        public void CloseFile(string filePath) { }

        public Task<LineChunkResult> ReadLineChunkAsync(string filePath, int lineNumber, int startColumn, int columnCount, CancellationToken cancellationToken = default)
            => Task.FromResult(new LineChunkResult(lineNumber, startColumn, "chunk", 80, false));

        public Task<List<int>> SearchInLargeLineAsync(string filePath, int lineNumber, string searchTerm, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<int>());
    }

    /// <summary>
    /// Mock IFileService that throws OperationCanceledException mid-scan,
    /// simulating what happens when PhotinoHostService cancels _scanCts.
    /// </summary>
    private class CancellingFileMockFileService : IFileService
    {
        public Task<FileOpenResult> OpenFileDialogAsync()
            => Task.FromResult(new FileOpenResult(false, null, "Not implemented"));

        public async Task<FileOpenMetadata> OpenFileAsync(
            string filePath,
            Action<FileOpenMetadata>? onPartialMetadata = null,
            IProgress<FileLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Simulate scan work then cancellation mid-flight
            await Task.Yield();
            throw new OperationCanceledException(cancellationToken);
        }

        public Task<FileOpenMetadata> RefreshFileAsync(string filePath, IProgress<FileLoadProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new FileOpenMetadata(filePath, Path.GetFileName(filePath), 100, 500_000, "UTF-8", 80));

        public Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default)
            => Task.FromResult(new LinesResult(startLine, new[] { "line" }, 100));

        public int GetTotalLines(string filePath) => 100;
        public int GetMaxLineLength(string filePath) => 80;
        public int GetLineCharLength(string filePath, int lineNumber) => 80;
        public void CloseFile(string filePath) { }

        public Task<LineChunkResult> ReadLineChunkAsync(string filePath, int lineNumber, int startColumn, int columnCount, CancellationToken cancellationToken = default)
            => Task.FromResult(new LineChunkResult(lineNumber, startColumn, "chunk", 80, false));

        public Task<List<int>> SearchInLargeLineAsync(string filePath, int lineNumber, string searchTerm, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<int>());
    }

    /// <summary>
    /// Mock IViewportService that returns valid ViewportResult.
    /// </summary>
    private class MockViewportService : IViewportService
    {
        public Task<ViewportResult> GetViewportAsync(
            string filePath, int startLine, int lineCount,
            int startColumn, int columnCount, bool wrapMode,
            int viewportColumns, CancellationToken cancellationToken = default)
        {
            var actualLineCount = Math.Max(1, lineCount);
            var lines = new string[actualLineCount];
            var lineLengths = new int[actualLineCount];
            for (int i = 0; i < actualLineCount; i++)
            {
                lines[i] = new string('x', Math.Min(columnCount, 80));
                lineLengths[i] = 80;
            }

            return Task.FromResult(new ViewportResult(
                Lines: lines,
                StartLine: startLine,
                StartColumn: startColumn,
                TotalPhysicalLines: 100,
                LineLengths: lineLengths,
                MaxLineLength: 200,
                TotalVirtualLines: null,
                Truncated: false));
        }

        public int GetVirtualLineCount(string filePath, int columnWidth) => 100;
        public int GetMaxLineLength(string filePath) => 200;
    }

    /// <summary>
    /// Property 2a: For all file sizes ≤ 256_000, OpenFileByPathAsync sends exactly one
    /// FileOpenedResponse(isPartial:false) and zero ViewportResponse messages.
    ///
    /// Small files do not go through partial metadata path → no viewport push expected.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 50)]
    public bool Preservation_SmallFileOpen_OneFileOpenedResponse_ZeroViewportResponse(PositiveInt fileSizeSeed, PositiveInt linesSeed, PositiveInt maxLineSeed)
    {
        // Generate: fileSize in [1, 256_000]
        var fileSize = (long)(1 + (fileSizeSeed.Get % 256_000));
        // Generate: totalLines in [1, 5000]
        var totalLines = 1 + (linesSeed.Get % 5000);
        // Generate: maxLineLength in [1, 5000]
        var maxLineLength = 1 + (maxLineSeed.Get % 5000);

        // Arrange
        var mockFileService = new SmallFileMockFileService(fileSize, totalLines, maxLineLength);
        var mockViewportService = new MockViewportService();
        var mockMessageRouter = new MockMessageRouter();

        var service = new PhotinoHostService(mockMessageRouter, mockFileService, mockViewportService);

        // Act
        var filePath = $"/tmp/test-small-file-{fileSize}.txt";
        service.OpenFileByPathAsync(filePath).GetAwaiter().GetResult();

        // Assert
        var messages = mockMessageRouter.SentMessages;

        // Exactly one FileOpenedResponse with isPartial=false
        var fileOpenedResponses = messages
            .Where(m => m.Message is FileOpenedResponse resp && !resp.IsPartial && !resp.IsRefresh)
            .ToList();

        if (fileOpenedResponses.Count != 1)
            return false;

        // Zero ViewportResponse messages
        var viewportResponses = messages
            .Where(m => m.Message is ViewportResponse)
            .ToList();

        return viewportResponses.Count == 0;
    }

    /// <summary>
    /// Property 2b: For all valid RequestViewport messages, HandleRequestViewportAsync
    /// sends exactly one ViewportResponse — no extra messages.
    ///
    /// On-demand viewport requests produce exactly one response.
    ///
    /// **Validates: Requirements 3.2**
    /// </summary>
    [Property(MaxTest = 50)]
    public bool Preservation_OnDemandViewport_ExactlyOneViewportResponse(
        PositiveInt startLineSeed, PositiveInt lineCountSeed,
        PositiveInt startColSeed, PositiveInt colCountSeed,
        bool wrapMode, PositiveInt vpColSeed)
    {
        // Generate valid viewport params
        var startLine = startLineSeed.Get % 1000;
        var lineCount = 1 + (lineCountSeed.Get % 200);
        var startColumn = startColSeed.Get % 500;
        var columnCount = 1 + (colCountSeed.Get % 500);
        var viewportColumns = 1 + (vpColSeed.Get % 500);

        // Arrange: need a file open first so HandleRequestViewportAsync works
        var mockFileService = new SmallFileMockFileService(100_000, 1000, 200);
        var mockViewportService = new MockViewportService();
        var mockMessageRouter = new MockMessageRouter();

        var service = new PhotinoHostService(mockMessageRouter, mockFileService, mockViewportService);

        // Open a file first (sets _currentFilePath)
        service.OpenFileByPathAsync("/tmp/test-viewport.txt").GetAwaiter().GetResult();

        // Clear messages from file open
        mockMessageRouter.SentMessages.Clear();

        // Act: simulate RequestViewport message
        var request = new RequestViewport
        {
            StartLine = startLine,
            LineCount = lineCount,
            StartColumn = startColumn,
            ColumnCount = columnCount,
            WrapMode = wrapMode,
            ViewportColumns = viewportColumns
        };

        mockMessageRouter.SimulateMessageAsync(request).GetAwaiter().GetResult();

        // Assert: exactly one ViewportResponse, no other messages
        var messages = mockMessageRouter.SentMessages;
        var viewportResponses = messages
            .Where(m => m.Message is ViewportResponse)
            .ToList();

        if (viewportResponses.Count != 1)
            return false;

        // No extra messages beyond the single ViewportResponse
        return messages.Count == 1;
    }

    /// <summary>
    /// Property 2c: When scan is cancelled mid-flight, no ViewportResponse is sent
    /// for the cancelled file. Also no FileOpenedResponse for cancelled file.
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Fact]
    public async Task Preservation_ScanCancellation_NoViewportResponseSent()
    {
        // Arrange: mock file service that throws OperationCanceledException mid-scan,
        // simulating what happens when PhotinoHostService cancels _scanCts
        // (e.g., user opens a new file while scan is in progress).

        var mockViewportService = new MockViewportService();
        var mockMessageRouter = new MockMessageRouter();
        var cancellingFileService = new CancellingFileMockFileService();

        var service = new PhotinoHostService(mockMessageRouter, cancellingFileService, mockViewportService);

        // Act: open file — the mock throws OperationCanceledException mid-scan
        await service.OpenFileByPathAsync("/tmp/test-cancel.txt");

        // Assert: no ViewportResponse sent for cancelled file
        var viewportResponses = mockMessageRouter.SentMessages
            .Where(m => m.Message is ViewportResponse)
            .ToList();

        Assert.Empty(viewportResponses);

        // No FileOpenedResponse for cancelled file either
        var fileOpenedResponses = mockMessageRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .ToList();

        Assert.Empty(fileOpenedResponses);
    }
}
