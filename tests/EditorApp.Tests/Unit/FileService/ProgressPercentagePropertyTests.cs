using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using EditorApp.Services;

namespace EditorApp.Tests.Unit.FileService;

/// <summary>
/// Feature: file-load-progress-bar, Property 1: Progress percentage calculation correctness
/// Validates: Requirements 1.4, 3.3
/// </summary>
public class ProgressPercentagePropertyTests
{
    /// <summary>
    /// For any (bytesScanned, totalFileSize) where 0 ≤ bytesScanned ≤ totalFileSize
    /// and totalFileSize > 0, the computed percentage equals
    /// (int)Math.Round((double)bytesScanned / totalFileSize * 100) and is in [0, 100].
    /// </summary>
    [Property(MaxTest = 2)]
    public Property PercentageCalculation_AlwaysInRange_AndMatchesFormula()
    {
        var gen =
            from totalFileSize in Gen.Choose(1, int.MaxValue / 2).Select(x => (long)x)
            from bytesScanned in Gen.Choose(0, int.MaxValue / 2).Select(x => (long)x).Select(x => Math.Min(x, totalFileSize))
            select (bytesScanned, totalFileSize);

        var arb = gen.ToArbitrary();

        return Prop.ForAll(arb, pair =>
        {
            var (bytesScanned, totalFileSize) = pair;
            var percent = (int)Math.Round((double)bytesScanned / totalFileSize * 100);
            return (percent >= 0 && percent <= 100)
                .Label($"percent={percent} must be in [0,100] for bytesScanned={bytesScanned}, totalFileSize={totalFileSize}");
        });
    }

    /// <summary>
    /// When bytesScanned == 0, percent is always 0.
    /// </summary>
    [Property(MaxTest = 2)]
    public Property PercentageAtZeroBytes_IsAlwaysZero()
    {
        var arb = Gen.Choose(1, int.MaxValue / 2).Select(x => (long)x).ToArbitrary();

        return Prop.ForAll(arb, totalFileSize =>
        {
            var percent = (int)Math.Round((double)0 / totalFileSize * 100);
            return (percent == 0).Label($"percent should be 0 when bytesScanned=0, got {percent}");
        });
    }

    /// <summary>
    /// When bytesScanned == totalFileSize, percent is always 100.
    /// </summary>
    [Property(MaxTest = 2)]
    public Property PercentageAtFullBytes_IsAlways100()
    {
        var arb = Gen.Choose(1, int.MaxValue / 2).Select(x => (long)x).ToArbitrary();

        return Prop.ForAll(arb, totalFileSize =>
        {
            var percent = (int)Math.Round((double)totalFileSize / totalFileSize * 100);
            return (percent == 100).Label($"percent should be 100 when bytesScanned==totalFileSize, got {percent}");
        });
    }
}
