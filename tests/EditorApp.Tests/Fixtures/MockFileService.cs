using EditorApp.Models;
using EditorApp.Services;

namespace EditorApp.Tests.Fixtures;

/// <summary>
/// Mock IFileService for testing PhotinoHostService.
/// Allows configuring behavior: delays, cancellation, errors, and progress reporting.
/// </summary>
public class MockFileService : IFileService
{
    /// <summary>
    /// When set, OpenFileAsync will report progress at these byte offsets before completing.
    /// Each entry is (percent, delayMs before reporting).
    /// </summary>
    public List<(int Percent, int DelayMs)> ProgressSteps { get; set; } = new();

    /// <summary>
    /// When set, OpenFileAsync will throw this exception at the specified step index.
    /// </summary>
    public (int StepIndex, Exception Exception)? ErrorInjection { get; set; }

    /// <summary>
    /// Metadata to return on successful completion.
    /// </summary>
    public FileOpenMetadata? ResultMetadata { get; set; }

    /// <summary>
    /// Tracks how many times OpenFileAsync was called.
    /// </summary>
    public int OpenFileCallCount { get; private set; }

    /// <summary>
    /// Set to true after OpenFileAsync completes or is cancelled, to verify resource cleanup.
    /// </summary>
    public bool WasDisposed { get; private set; }

    /// <summary>
    /// Delay in ms before starting the scan (simulates slow I/O).
    /// </summary>
    public int InitialDelayMs { get; set; }

    /// <summary>
    /// File size to use in progress reports.
    /// </summary>
    public long FileSizeBytes { get; set; } = 1_000_000;

    /// <summary>
    /// File name to use in progress reports.
    /// </summary>
    public string FileName { get; set; } = "test.txt";

    public Task<FileOpenResult> OpenFileDialogAsync()
    {
        return Task.FromResult(new FileOpenResult(false, null, "Not implemented in mock"));
    }

    public async Task<FileOpenMetadata> OpenFileAsync(
        string filePath,
        Action<FileOpenMetadata>? onPartialMetadata = null,
        IProgress<FileLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        OpenFileCallCount++;

        if (InitialDelayMs > 0)
        {
            await Task.Delay(InitialDelayMs, cancellationToken);
        }

        for (int i = 0; i < ProgressSteps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check if we should inject an error at this step
            if (ErrorInjection.HasValue && ErrorInjection.Value.StepIndex == i)
            {
                throw ErrorInjection.Value.Exception;
            }

            var (percent, delayMs) = ProgressSteps[i];

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }

            progress?.Report(new FileLoadProgress(FileName, percent, FileSizeBytes));
        }

        cancellationToken.ThrowIfCancellationRequested();

        WasDisposed = true;

        return ResultMetadata ?? new FileOpenMetadata(
            filePath, FileName, 100, FileSizeBytes, "UTF-8");
    }

    public Task<FileOpenMetadata> RefreshFileAsync(
        string filePath,
        IProgress<FileLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return OpenFileAsync(filePath, onPartialMetadata: null, progress, cancellationToken);
    }

    public Task<LinesResult> ReadLinesAsync(string filePath, int startLine, int lineCount, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LinesResult(startLine, new[] { "line1" }, 100));
    }

    public void CloseFile(string filePath) { }

    public int GetLineCharLength(string filePath, int lineNumber) => 80;

    public Task<LineChunkResult> ReadLineChunkAsync(string filePath, int lineNumber, int startColumn, int columnCount, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LineChunkResult(lineNumber, startColumn, "chunk", 80, false));
    }

    public Task<List<int>> SearchInLargeLineAsync(string filePath, int lineNumber, string searchTerm, CancellationToken cancellationToken = default)
        => Task.FromResult(new List<int>());
}
