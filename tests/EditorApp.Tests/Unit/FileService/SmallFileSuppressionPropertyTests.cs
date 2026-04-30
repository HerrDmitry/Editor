using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.FileService;

/// <summary>
/// Feature: file-load-progress-bar, Property 4: Small file suppression at threshold boundary
/// Validates: Requirements 2.1, 8.1, 8.2
/// </summary>
public class SmallFileSuppressionPropertyTests
{
    /// <summary>
    /// For any file with size ≤ 256,000 bytes, zero progress messages are emitted.
    /// Generate sizes in [1, 256000].
    /// </summary>
    [Property(MaxTest = 2)]
    public Property SmallFiles_EmitZeroProgressMessages()
    {
        var arb = Gen.Choose(1, (int)Services.FileService.SizeThresholdBytes)
            .Select(x => (long)x)
            .ToArbitrary();

        return Prop.ForAll(arb, fileSize =>
        {
            var sut = new Services.FileService();
            var reports = new List<FileLoadProgress>();
            var progress = new Progress<FileLoadProgress>(r => reports.Add(r));

            var path = CreateTempFileOfSize(fileSize);
            try
            {
                sut.OpenFileAsync(path, progress).GetAwaiter().GetResult();
                // Allow Progress<T> callbacks to complete (they're posted to sync context)
                Thread.Sleep(50);
                return (reports.Count == 0)
                    .Label($"Expected 0 progress messages for file size {fileSize}, got {reports.Count}");
            }
            finally
            {
                TempFileHelper.Cleanup(path);
            }
        });
    }

    /// <summary>
    /// For any file with size > 256,000 bytes, at least 2 progress messages (0% and 100%) are emitted.
    /// Generate sizes in [256001, 512000].
    /// </summary>
    [Property(MaxTest = 2)]
    public Property LargeFiles_EmitAtLeastTwoProgressMessages()
    {
        var arb = Gen.Choose((int)Services.FileService.SizeThresholdBytes + 1, 512_000)
            .Select(x => (long)x)
            .ToArbitrary();

        return Prop.ForAll(arb, fileSize =>
        {
            var sut = new Services.FileService();
            var reports = new List<FileLoadProgress>();
            var progress = new Progress<FileLoadProgress>(r => reports.Add(r));

            var path = CreateTempFileOfSize(fileSize);
            try
            {
                sut.OpenFileAsync(path, progress).GetAwaiter().GetResult();
                // Allow Progress<T> callbacks to complete
                Thread.Sleep(100);
                return (reports.Count >= 2)
                    .Label($"Expected ≥2 progress messages for file size {fileSize}, got {reports.Count}");
            }
            finally
            {
                TempFileHelper.Cleanup(path);
            }
        });
    }

    /// <summary>
    /// Boundary: file exactly at threshold (256,000) → zero messages.
    /// File at threshold+1 (256,001) → ≥2 messages.
    /// </summary>
    [Property(MaxTest = 2)]
    public Property BoundaryFiles_CorrectBehavior()
    {
        // Generate offset in {-1, 0, 1} relative to threshold
        var arb = Gen.Elements(-1, 0, 1).ToArbitrary();

        return Prop.ForAll(arb, offset =>
        {
            var fileSize = Services.FileService.SizeThresholdBytes + offset;
            if (fileSize <= 0) return true.Label("skip negative sizes");

            var sut = new Services.FileService();
            var reports = new List<FileLoadProgress>();
            var progress = new Progress<FileLoadProgress>(r => reports.Add(r));

            var path = CreateTempFileOfSize(fileSize);
            try
            {
                sut.OpenFileAsync(path, progress).GetAwaiter().GetResult();
                Thread.Sleep(100);

                if (fileSize <= Services.FileService.SizeThresholdBytes)
                {
                    return (reports.Count == 0)
                        .Label($"Expected 0 messages at threshold boundary (size={fileSize}), got {reports.Count}");
                }
                else
                {
                    return (reports.Count >= 2)
                        .Label($"Expected ≥2 messages above threshold (size={fileSize}), got {reports.Count}");
                }
            }
            finally
            {
                TempFileHelper.Cleanup(path);
            }
        });
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
