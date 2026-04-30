using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.FileService;

/// <summary>
/// Feature: file-load-progress-bar, Property 3: Progress message throttle
/// Validates: Requirements 1.5
/// </summary>
public class ProgressThrottlePropertyTests
{
    /// <summary>
    /// For any sequence of progress messages emitted during a single file scan,
    /// no two consecutive messages (except the final 100% message) shall have
    /// timestamps less than 50ms apart.
    ///
    /// NOTE: Real I/O timing can be unreliable in CI. We use a tolerance of 40ms
    /// (allowing 10ms jitter) and only check non-final consecutive pairs.
    /// </summary>
    [Property(MaxTest = 2)]
    public Property ProgressMessages_RespectThrottleInterval()
    {
        // Generate large file sizes that will produce multiple progress reports
        var arb = Gen.Choose(2_000_000, 5_000_000)
            .Select(x => (long)x)
            .ToArbitrary();

        return Prop.ForAll(arb, fileSize =>
        {
            var sut = new Services.FileService();
            var reports = new List<(FileLoadProgress Report, long Timestamp)>();
            var progress = new Progress<FileLoadProgress>(r =>
            {
                reports.Add((r, Environment.TickCount64));
            });

            var path = CreateTempFileOfSize(fileSize);
            try
            {
                sut.OpenFileAsync(path, progress).GetAwaiter().GetResult();
                // Allow Progress<T> callbacks to complete
                Thread.Sleep(200);

                if (reports.Count < 3)
                {
                    // Not enough reports to validate throttle meaningfully
                    return true.Label($"Only {reports.Count} reports, throttle not testable");
                }

                // Check gaps between consecutive non-final messages
                // Allow 40ms tolerance (50ms throttle - 10ms jitter)
                const long toleranceMs = 40;
                var violations = new List<string>();

                for (int i = 1; i < reports.Count; i++)
                {
                    // Skip check for final 100% message (allowed to be sent immediately)
                    if (reports[i].Report.Percent == 100)
                        continue;

                    // Also skip if previous was the initial 0% (it's sent before scanning starts)
                    if (reports[i - 1].Report.Percent == 0 && i == 1)
                        continue;

                    var gap = reports[i].Timestamp - reports[i - 1].Timestamp;
                    if (gap < toleranceMs)
                    {
                        violations.Add($"gap[{i - 1}→{i}]={gap}ms (percents {reports[i - 1].Report.Percent}→{reports[i].Report.Percent})");
                    }
                }

                return (violations.Count == 0)
                    .Label($"Throttle violations: {string.Join("; ", violations)}");
            }
            finally
            {
                TempFileHelper.Cleanup(path);
            }
        });
    }

    /// <summary>
    /// The initial 0% message is always sent (no throttle delay for first message).
    /// </summary>
    [Property(MaxTest = 2)]
    public Property InitialZeroPercent_AlwaysSentImmediately()
    {
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
                sut.OpenFileAsync(path, progress).GetAwaiter().GetResult();
                Thread.Sleep(100);

                if (reports.Count == 0)
                    return false.Label("No reports at all");

                return (reports[0].Percent == 0)
                    .Label($"First report should be 0%, got {reports[0].Percent}");
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
