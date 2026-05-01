using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.FileService;

/// <summary>
/// Feature: file-load-progress-bar, Property 2: Large file progress message sequence integrity
/// Validates: Requirements 1.1, 1.2, 1.3
/// </summary>
public class ProgressSequencePropertyTests
{
    /// <summary>
    /// For any file with size > 256,000 bytes that completes scanning without error,
    /// the sequence of progress messages starts with percent=0, ends with percent=100,
    /// and all intermediate percent values are monotonically non-decreasing.
    /// </summary>
    [Property(MaxTest = 2)]
    public Property LargeFile_ProgressSequence_StartsAtZero_EndsAt100_Monotonic()
    {
        // Generate file sizes between threshold+1 and 1MB
        var arb = Gen.Choose((int)Services.FileService.SizeThresholdBytes + 1, 1_000_000)
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
                sut.OpenFileAsync(path, progress: progress).GetAwaiter().GetResult();
                // Allow Progress<T> callbacks to complete
                Thread.Sleep(150);

                if (reports.Count < 2)
                    return false.Label($"Expected ≥2 reports for size {fileSize}, got {reports.Count}");

                // First message must be 0%
                var firstIs0 = reports[0].Percent == 0;

                // Last message must be 100%
                var lastIs100 = reports[^1].Percent == 100;

                // All values monotonically non-decreasing
                var monotonic = true;
                for (int i = 1; i < reports.Count; i++)
                {
                    if (reports[i].Percent < reports[i - 1].Percent)
                    {
                        monotonic = false;
                        break;
                    }
                }

                return (firstIs0 && lastIs100 && monotonic)
                    .Label($"first=0:{firstIs0}, last=100:{lastIs100}, monotonic:{monotonic}, " +
                           $"sequence=[{string.Join(",", reports.Select(r => r.Percent))}]");
            }
            finally
            {
                TempFileHelper.Cleanup(path);
            }
        });
    }

    /// <summary>
    /// All percent values in the sequence are within [0, 100].
    /// </summary>
    [Property(MaxTest = 2)]
    public Property LargeFile_AllPercentValues_InValidRange()
    {
        var arb = Gen.Choose((int)Services.FileService.SizeThresholdBytes + 1, 800_000)
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
                sut.OpenFileAsync(path, progress: progress).GetAwaiter().GetResult();
                Thread.Sleep(150);

                var allInRange = reports.All(r => r.Percent >= 0 && r.Percent <= 100);
                return allInRange.Label(
                    $"All percents must be in [0,100]. Got: [{string.Join(",", reports.Select(r => r.Percent))}]");
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
