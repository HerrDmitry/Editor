using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests;

/// <summary>
/// Bug condition exploration test for viewport-scan-complete-refresh.
///
/// Property 1: Bug Condition - No ViewportResponse After Large File Scan Completion
///
/// WHEN a large file (>256 KB) scan completes (partial metadata emitted, then final metadata sent)
/// THEN the system SHALL send a ViewportResponse after the final FileOpenedResponse(isPartial=false)
///
/// This test MUST FAIL on unfixed code — failure confirms the bug exists.
/// DO NOT fix the test or code when it fails.
///
/// Validates: Requirements 1.1, 1.2, 2.1, 2.2
/// </summary>
public class ViewportScanCompleteBugConditionTests
{
    /// <summary>
    /// Mock IFileService that simulates large file scan:
    /// - Invokes onPartialMetadata callback (indicating >256 KB file)
    /// - Returns final FileOpenMetadata with definitive totals
    /// </summary>
    private class LargeFileMockFileService : IFileService
    {
        private readonly long _fileSizeBytes;
        private readonly int _partialTotalLines;
        private readonly int _finalTotalLines;
        private readonly int _maxLineLength;

        public LargeFileMockFileService(long fileSizeBytes, int partialTotalLines, int finalTotalLines, int maxLineLength)
        {
            _fileSizeBytes = fileSizeBytes;
            _partialTotalLines = partialTotalLines;
            _finalTotalLines = finalTotalLines;
            _maxLineLength = maxLineLength;
        }

        public Task<FileOpenResult> OpenFileDialogAsync()
            => Task.FromResult(new FileOpenResult(false, null, "Not implemented"));

        public async Task<FileOpenMetadata> OpenFileAsync(
            string filePath,
            Action<FileOpenMetadata>? onPartialMetadata = null,
            IProgress<FileLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Simulate large file: invoke onPartialMetadata with provisional data
            var partialMeta = new FileOpenMetadata(
                filePath,
                Path.GetFileName(filePath),
                _partialTotalLines,
                _fileSizeBytes,
                "UTF-8",
                _maxLineLength / 2);

            onPartialMetadata?.Invoke(partialMeta);

            // Small yield to simulate async scan work
            await Task.Yield();

            cancellationToken.ThrowIfCancellationRequested();

            // Return final metadata (scan complete)
            return new FileOpenMetadata(
                filePath,
                Path.GetFileName(filePath),
                _finalTotalLines,
                _fileSizeBytes,
                "UTF-8",
                _maxLineLength);
        }

        public Task<FileOpenMetadata> RefreshFileAsync(
            string filePath,
            IProgress<FileLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new FileOpenMetadata(filePath, Path.GetFileName(filePath), _finalTotalLines, _fileSizeBytes, "UTF-8", _maxLineLength));

        public Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default)
            => Task.FromResult(new LinesResult(startLine, new[] { "line content" }, _finalTotalLines));

        public int GetTotalLines(string filePath) => _finalTotalLines;
        public int GetMaxLineLength(string filePath) => _maxLineLength;
        public int GetLineCharLength(string filePath, int lineNumber) => 80;
        public void CloseFile(string filePath) { }

        public Task<LineChunkResult> ReadLineChunkAsync(string filePath, int lineNumber, int startColumn, int columnCount, CancellationToken cancellationToken = default)
            => Task.FromResult(new LineChunkResult(lineNumber, startColumn, "chunk", 80, false));

        public Task<List<int>> SearchInLargeLineAsync(string filePath, int lineNumber, string searchTerm, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<int>());
    }

    /// <summary>
    /// Mock IViewportService that returns a valid ViewportResult.
    /// </summary>
    private class MockViewportService : IViewportService
    {
        private readonly int _totalPhysicalLines;
        private readonly int _maxLineLength;

        public MockViewportService(int totalPhysicalLines, int maxLineLength)
        {
            _totalPhysicalLines = totalPhysicalLines;
            _maxLineLength = maxLineLength;
        }

        public Task<ViewportResult> GetViewportAsync(
            string filePath, int startLine, int lineCount,
            int startColumn, int columnCount, bool wrapMode,
            int viewportColumns, CancellationToken cancellationToken = default)
        {
            var actualLineCount = Math.Max(0, Math.Min(lineCount, _totalPhysicalLines - startLine));
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
                TotalPhysicalLines: _totalPhysicalLines,
                LineLengths: lineLengths,
                MaxLineLength: _maxLineLength,
                TotalVirtualLines: null,
                Truncated: false));
        }

        public int GetVirtualLineCount(string filePath, int columnWidth) => _totalPhysicalLines;
        public int GetMaxLineLength(string filePath) => _maxLineLength;
    }

    /// <summary>
    /// Property 1: Bug Condition - No ViewportResponse After Large File Scan Completion
    ///
    /// For any large file (fileSize > 256_000) where partial metadata is emitted and scan
    /// completes successfully, the system SHALL send a ViewportResponse after the final
    /// FileOpenedResponse(isPartial=false).
    ///
    /// Bug condition: fileSize > 256_000 AND partialMetadataWasEmitted AND NOT viewportResponseSentAfterScanComplete
    ///
    /// Expected behavior: ViewportResponse with totalPhysicalLines == finalTotalLines
    /// and startLine >= 0 is sent after final FileOpenedResponse.
    ///
    /// EXPECTED OUTCOME: Test FAILS on unfixed code (proves bug exists).
    ///
    /// **Validates: Requirements 1.1, 1.2, 2.1, 2.2**
    /// </summary>
    [Property(MaxTest = 50)]
    public bool BugCondition_NoViewportResponseAfterLargeFileScanComplete(PositiveInt fileSizeSeed, PositiveInt linesSeed, PositiveInt maxLineSeed)
    {
        // Generate: fileSize > 256_000 (range 256_001 to 10_000_000)
        var fileSize = (long)(256_001 + (fileSizeSeed.Get % 9_743_999));
        // Generate: partialLines in [50, 2000]
        var partialLines = 50 + (linesSeed.Get % 1951);
        // Generate: finalLines >= partialLines, up to partialLines * 5
        var finalLines = partialLines + (linesSeed.Get % (partialLines * 4 + 1));
        // Generate: maxLineLength in [10, 5000]
        var maxLineLength = 10 + (maxLineSeed.Get % 4991);

        // Arrange
        var mockFileService = new LargeFileMockFileService(fileSize, partialLines, finalLines, maxLineLength);
        var mockViewportService = new MockViewportService(finalLines, maxLineLength);
        var mockMessageRouter = new MockMessageRouter();

        // Create PhotinoHostService via internal test constructor
        var service = new PhotinoHostService(mockMessageRouter, mockFileService, mockViewportService);

        // Act: open a large file
        var filePath = $"/tmp/test-large-file-{fileSize}.txt";
        service.OpenFileByPathAsync(filePath).GetAwaiter().GetResult();

        // Analyze sent messages
        var messages = mockMessageRouter.SentMessages;

        // Find the final FileOpenedResponse (isPartial=false, isRefresh=false)
        var finalFileOpenedIndex = -1;
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].Message is FileOpenedResponse resp && !resp.IsPartial && !resp.IsRefresh)
            {
                finalFileOpenedIndex = i;
            }
        }

        // Must have final FileOpenedResponse
        if (finalFileOpenedIndex < 0)
            return false;

        // Assert: a ViewportResponse exists AFTER the final FileOpenedResponse
        for (int i = finalFileOpenedIndex + 1; i < messages.Count; i++)
        {
            if (messages[i].Message is ViewportResponse vr)
            {
                // Verify correctness of the viewport response
                return vr.TotalPhysicalLines == finalLines && vr.StartLine >= 0;
            }
        }

        // No ViewportResponse found after final FileOpenedResponse — bug condition confirmed
        return false;
    }

    /// <summary>
    /// Deterministic unit test for the same bug condition — easier to read counterexample.
    /// Opens a 300KB file, verifies ViewportResponse follows final FileOpenedResponse.
    ///
    /// **Validates: Requirements 1.1, 1.2, 2.1, 2.2**
    /// </summary>
    [Fact]
    public async Task BugCondition_Deterministic_LargeFile_NoViewportAfterScanComplete()
    {
        // Arrange: 300KB file, 200 partial lines, 500 final lines
        var fileSize = 300_000L;
        var partialLines = 200;
        var finalLines = 500;
        var maxLineLength = 120;

        var mockFileService = new LargeFileMockFileService(fileSize, partialLines, finalLines, maxLineLength);
        var mockViewportService = new MockViewportService(finalLines, maxLineLength);
        var mockMessageRouter = new MockMessageRouter();

        var service = new PhotinoHostService(mockMessageRouter, mockFileService, mockViewportService);

        // Act
        await service.OpenFileByPathAsync("/tmp/test-large-file.txt");

        // Analyze messages
        var messages = mockMessageRouter.SentMessages;

        // Find final FileOpenedResponse (isPartial=false)
        var finalFileOpenedIndex = -1;
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].Message is FileOpenedResponse resp && !resp.IsPartial && !resp.IsRefresh)
            {
                finalFileOpenedIndex = i;
            }
        }

        Assert.True(finalFileOpenedIndex >= 0,
            "Expected final FileOpenedResponse(isPartial=false) to be sent");

        // Assert: ViewportResponse exists after final FileOpenedResponse
        var viewportAfterFinal = messages
            .Skip(finalFileOpenedIndex + 1)
            .Any(m => m.Message is ViewportResponse);

        Assert.True(viewportAfterFinal,
            $"Bug confirmed: For fileSize={fileSize}, totalLines={finalLines}: " +
            $"messages contain FileOpenedResponse(isPartial:false) but no subsequent ViewportResponse. " +
            $"All messages: [{string.Join(", ", messages.Select(m => m.TypeName))}]");
    }
}
