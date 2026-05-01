using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.FileService;

/// <summary>
/// Unit tests for FileService progress reporting and cancellation.
/// Validates: Requirements 2.1, 8.1, 9.1, 9.2, 9.4, 9.5
/// </summary>
public class ProgressAndCancellationTests
{
    [Fact]
    public async Task SmallFile_100Bytes_ProducesNoProgressMessages()
    {
        var sut = new Services.FileService();
        var reports = new List<FileLoadProgress>();
        var progress = new Progress<FileLoadProgress>(r => reports.Add(r));

        var path = CreateTempFileOfSize(100);
        try
        {
            await sut.OpenFileAsync(path, progress: progress);
            await Task.Delay(50); // Allow Progress<T> callbacks to post

            Assert.Empty(reports);
        }
        finally
        {
            TempFileHelper.Cleanup(path);
        }
    }

    [Fact]
    public async Task FileExactlyAtThreshold_256000Bytes_ProducesNoProgress()
    {
        var sut = new Services.FileService();
        var reports = new List<FileLoadProgress>();
        var progress = new Progress<FileLoadProgress>(r => reports.Add(r));

        var path = CreateTempFileOfSize(Services.FileService.SizeThresholdBytes);
        try
        {
            await sut.OpenFileAsync(path, progress: progress);
            await Task.Delay(50);

            Assert.Empty(reports);
        }
        finally
        {
            TempFileHelper.Cleanup(path);
        }
    }

    [Fact]
    public async Task FileAtThresholdPlusOne_256001Bytes_ProducesProgress()
    {
        var sut = new Services.FileService();
        var reports = new List<FileLoadProgress>();
        var progress = new Progress<FileLoadProgress>(r => reports.Add(r));

        var path = CreateTempFileOfSize(Services.FileService.SizeThresholdBytes + 1);
        try
        {
            await sut.OpenFileAsync(path, progress: progress);
            await Task.Delay(100);

            Assert.True(reports.Count >= 2, $"Expected ≥2 progress reports, got {reports.Count}");
            Assert.Equal(0, reports[0].Percent);
            Assert.Equal(100, reports[^1].Percent);
        }
        finally
        {
            TempFileHelper.Cleanup(path);
        }
    }

    [Fact]
    public async Task Cancellation_StopsProgressAndReleasesFileHandle()
    {
        var sut = new Services.FileService();
        var reports = new List<FileLoadProgress>();
        var progress = new Progress<FileLoadProgress>(r => reports.Add(r));
        var cts = new CancellationTokenSource();

        // Use a large file so scanning takes time
        var path = CreateTempFileOfSize(5_000_000);
        try
        {
            // Cancel after a short delay
            cts.CancelAfter(TimeSpan.FromMilliseconds(10));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await sut.OpenFileAsync(path, progress: progress, cancellationToken: cts.Token);
            });

            // File handle should be released — verify by opening the file again
            await using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.NotNull(stream); // If we get here, handle was released
        }
        finally
        {
            TempFileHelper.Cleanup(path);
        }
    }

    [Fact]
    public async Task AfterCancellation_NewFileOpensNormally()
    {
        var sut = new Services.FileService();
        var cts = new CancellationTokenSource();

        // First file: large, will be cancelled
        var path1 = CreateTempFileOfSize(5_000_000);
        // Second file: normal size above threshold
        var path2 = CreateTempFileOfSize(300_000);

        try
        {
            // Cancel first file quickly
            cts.CancelAfter(TimeSpan.FromMilliseconds(10));

            try
            {
                await sut.OpenFileAsync(path1, cancellationToken: cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            // Now open second file normally — should succeed
            var reports = new List<FileLoadProgress>();
            var progress = new Progress<FileLoadProgress>(r => reports.Add(r));

            var metadata = await sut.OpenFileAsync(path2, progress: progress);
            await Task.Delay(100);

            Assert.NotNull(metadata);
            Assert.Equal(path2, metadata.FilePath);
            Assert.True(reports.Count >= 2, "Second file should produce progress");
        }
        finally
        {
            TempFileHelper.Cleanup(path1);
            TempFileHelper.Cleanup(path2);
        }
    }

    private static string CreateTempFileOfSize(long size)
    {
        var path = Path.GetTempFileName();
        var data = new byte[size];
        Array.Fill(data, (byte)'A');
        File.WriteAllBytes(path, data);
        return path;
    }
}
