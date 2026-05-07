using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.PhotinoHostService;

/// <summary>
/// Unit tests for PhotinoHostService partial + final response flow.
/// Validates: Requirements 1.2, 1.3, 2.3, 9.1
/// </summary>
public class PartialResponseFlowUnitTests
{
    /// <summary>
    /// Test: Large file sends partial then final FileOpenedResponse.
    /// Partial has IsPartial=true, final has IsPartial=false.
    /// Validates: Requirements 1.2, 2.3
    /// </summary>
    [Fact]
    public async Task LargeFile_SendsPartialThenFinalResponse()
    {
        var mockRouter = new MockMessageRouter();
        var mockFileService = new PartialEmittingFileService(
            emitPartial: true,
            partialMetadata: new FileOpenMetadata("/path/large.txt", "large.txt", 500, 512_000, "UTF-8"),
            finalMetadata: new FileOpenMetadata("/path/large.txt", "large.txt", 10_000, 512_000, "UTF-8"));

        var sut = new Services.PhotinoHostService(mockRouter, mockFileService);

        await sut.OpenFileByPathAsync("/path/large.txt");

        // Get all FileOpenedResponse messages
        var responses = mockRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();

        // Should have exactly 2: partial then final
        Assert.Equal(2, responses.Count);

        // First response is partial
        Assert.True(responses[0].IsPartial);
        Assert.Equal("large.txt", responses[0].FileName);
        Assert.Equal(500, responses[0].TotalLines);
        Assert.Equal(512_000, responses[0].FileSizeBytes);
        Assert.Equal("UTF-8", responses[0].Encoding);

        // Second response is final
        Assert.False(responses[1].IsPartial);
        Assert.Equal("large.txt", responses[1].FileName);
        Assert.Equal(10_000, responses[1].TotalLines);
        Assert.Equal(512_000, responses[1].FileSizeBytes);
        Assert.Equal("UTF-8", responses[1].Encoding);
    }

    /// <summary>
    /// Test: Small file sends only final FileOpenedResponse (no partial).
    /// Validates: Requirements 1.3
    /// </summary>
    [Fact]
    public async Task SmallFile_SendsOnlyFinalResponse()
    {
        var mockRouter = new MockMessageRouter();
        var mockFileService = new PartialEmittingFileService(
            emitPartial: false,
            partialMetadata: null,
            finalMetadata: new FileOpenMetadata("/path/small.txt", "small.txt", 50, 1_024, "UTF-8"));

        var sut = new Services.PhotinoHostService(mockRouter, mockFileService);

        await sut.OpenFileByPathAsync("/path/small.txt");

        // Get all FileOpenedResponse messages
        var responses = mockRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();

        // Should have exactly 1: final only
        Assert.Single(responses);
        Assert.False(responses[0].IsPartial);
        Assert.Equal("small.txt", responses[0].FileName);
        Assert.Equal(50, responses[0].TotalLines);
        Assert.Equal(1_024, responses[0].FileSizeBytes);
        Assert.Equal("UTF-8", responses[0].Encoding);
    }

    /// <summary>
    /// Test: Cancellation during scan sends no final response.
    /// When scan is cancelled (e.g. new file opened), no FileOpenedResponse is sent.
    /// Validates: Requirement 9.1
    /// </summary>
    [Fact]
    public async Task CancellationDuringScan_NoFinalResponseSent()
    {
        var mockRouter = new MockMessageRouter();
        var blockingFileService = new CancellablePartialFileService();

        var sut = new Services.PhotinoHostService(mockRouter, blockingFileService);

        // Start first file open (will block until cancelled)
        var firstOpen = sut.OpenFileByPathAsync("/path/file1.txt");

        // Wait for blocking service to start
        await blockingFileService.WaitUntilStarted();

        // Open second file — cancels first scan
        var secondOpen = sut.OpenFileByPathAsync("/path/file2.txt");

        await firstOpen;
        await secondOpen;

        // Get all FileOpenedResponse messages
        var responses = mockRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();

        // Only the second file should have a response (no partial or final for cancelled file1)
        Assert.All(responses, r => Assert.Equal("file2.txt", r.FileName));

        // No error messages for cancelled scan
        Assert.Empty(mockRouter.ErrorMessages);
    }

    /// <summary>
    /// Mock IFileService that invokes onPartialMetadata callback when configured.
    /// Used to test partial + final response flow in PhotinoHostService.
    /// </summary>
    private class PartialEmittingFileService : IFileService
    {
        private readonly bool _emitPartial;
        private readonly FileOpenMetadata? _partialMetadata;
        private readonly FileOpenMetadata _finalMetadata;

        public PartialEmittingFileService(
            bool emitPartial,
            FileOpenMetadata? partialMetadata,
            FileOpenMetadata finalMetadata)
        {
            _emitPartial = emitPartial;
            _partialMetadata = partialMetadata;
            _finalMetadata = finalMetadata;
        }

        public Task<FileOpenResult> OpenFileDialogAsync()
            => Task.FromResult(new FileOpenResult(false, null, "mock"));

        public Task<FileOpenMetadata> OpenFileAsync(
            string filePath,
            Action<FileOpenMetadata>? onPartialMetadata = null,
            IProgress<FileLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (_emitPartial && onPartialMetadata != null && _partialMetadata != null)
            {
                onPartialMetadata(_partialMetadata);
            }

            return Task.FromResult(_finalMetadata);
        }

        public Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default)
            => Task.FromResult(new LinesResult(startLine, new[] { "line" }, 100));

        public Task<FileOpenMetadata> RefreshFileAsync(string filePath, IProgress<FileLoadProgress>? progress = null, CancellationToken cancellationToken = default)
            => OpenFileAsync(filePath, onPartialMetadata: null, progress, cancellationToken);

        public void CloseFile(string filePath) { }

        public int GetLineCharLength(string filePath, int lineNumber) => 80;

        public Task<LineChunkResult> ReadLineChunkAsync(string filePath, int lineNumber, int startColumn, int columnCount, CancellationToken cancellationToken = default)
            => Task.FromResult(new LineChunkResult(lineNumber, startColumn, "chunk", 80, false));

        public Task<List<int>> SearchInLargeLineAsync(string filePath, int lineNumber, string searchTerm, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<int>());
    }

    /// <summary>
    /// Mock IFileService that blocks on first call until cancelled.
    /// Second call returns immediately. Used to test cancellation flow.
    /// </summary>
    private class CancellablePartialFileService : IFileService
    {
        private readonly TaskCompletionSource _started = new();
        private int _callCount;

        public Task WaitUntilStarted() => _started.Task;

        public Task<FileOpenResult> OpenFileDialogAsync()
            => Task.FromResult(new FileOpenResult(false, null, "mock"));

        public async Task<FileOpenMetadata> OpenFileAsync(
            string filePath,
            Action<FileOpenMetadata>? onPartialMetadata = null,
            IProgress<FileLoadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _callCount);

            if (call == 1)
            {
                // First call: signal started, then block until cancelled
                _started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                // Unreachable
                return new FileOpenMetadata(filePath, Path.GetFileName(filePath), 10, 1000, "UTF-8");
            }

            // Second call: return immediately
            return new FileOpenMetadata(filePath, Path.GetFileName(filePath), 50, 2000, "UTF-8");
        }

        public Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default)
            => Task.FromResult(new LinesResult(startLine, new[] { "line" }, 50));

        public Task<FileOpenMetadata> RefreshFileAsync(string filePath, IProgress<FileLoadProgress>? progress = null, CancellationToken cancellationToken = default)
            => OpenFileAsync(filePath, onPartialMetadata: null, progress, cancellationToken);

        public void CloseFile(string filePath) { }

        public int GetLineCharLength(string filePath, int lineNumber) => 80;

        public Task<LineChunkResult> ReadLineChunkAsync(string filePath, int lineNumber, int startColumn, int columnCount, CancellationToken cancellationToken = default)
            => Task.FromResult(new LineChunkResult(lineNumber, startColumn, "chunk", 80, false));

        public Task<List<int>> SearchInLargeLineAsync(string filePath, int lineNumber, string searchTerm, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<int>());
    }
}
