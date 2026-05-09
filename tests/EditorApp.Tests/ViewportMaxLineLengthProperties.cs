using EditorApp.Services;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests;

/// <summary>
/// Property-based tests for maximum line length correctness.
/// Feature: viewport-text-rendering, Property 9: Maximum line length correctness
/// </summary>
public class ViewportMaxLineLengthProperties
{
    /// <summary>
    /// Feature: viewport-text-rendering, Property 9: Maximum line length correctness
    ///
    /// For any random file content (multiple lines with varying lengths including empty lines),
    /// the MaxLineLength reported by FileService.OpenFileAsync SHALL be greater than or equal to
    /// the actual maximum character length across all lines. This holds because MaxLineLength is
    /// computed from byte-length deltas between consecutive line offsets, and for UTF-8,
    /// byte length >= char length always.
    ///
    /// **Validates: Requirements 4.5, 10.1, 10.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MaxLineLengthIsConservativeOverestimate(PositiveInt lineCountSeed, int contentSeed)
    {
        // Generate between 1 and 200 lines
        int lineCount = (lineCountSeed.Get % 200) + 1;
        var rng = new Random(contentSeed);

        // Generate random lines with varying lengths (0 to 500 chars)
        // Include ASCII and some multibyte UTF-8 characters
        var lines = new string[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            int lineLen = rng.Next(0, 501); // 0 = empty line
            lines[i] = GenerateRandomLine(rng, lineLen);
        }

        // Write content to temp file
        var tempFile = Path.Combine(Path.GetTempPath(), $"maxlinelen_prop9_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, string.Join("\n", lines));

            // Open file via FileService
            var fileService = new FileService();
            var metadata = fileService.OpenFileAsync(tempFile).GetAwaiter().GetResult();

            // Compute actual max character length across all lines
            int actualMaxCharLength = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > actualMaxCharLength)
                    actualMaxCharLength = lines[i].Length;
            }

            // Property: MaxLineLength >= actual max char length (conservative overestimate)
            return metadata.MaxLineLength >= actualMaxCharLength;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Generate a random line with a mix of ASCII and multibyte UTF-8 characters.
    /// </summary>
    private static string GenerateRandomLine(Random rng, int targetLength)
    {
        if (targetLength == 0)
            return string.Empty;

        var chars = new char[targetLength];
        for (int i = 0; i < targetLength; i++)
        {
            int kind = rng.Next(100);
            if (kind < 70)
            {
                // ASCII printable (0x20-0x7E)
                chars[i] = (char)rng.Next(0x20, 0x7F);
            }
            else if (kind < 85)
            {
                // 2-byte UTF-8 chars (Latin Extended, Cyrillic, etc: U+0080-U+07FF)
                chars[i] = (char)rng.Next(0x80, 0x0800);
            }
            else if (kind < 95)
            {
                // 3-byte UTF-8 chars (CJK, etc: U+0800-U+FFFF, excluding surrogates)
                int c = rng.Next(0x0800, 0xFFFF);
                // Skip surrogate range (0xD800-0xDFFF)
                if (c >= 0xD800 && c <= 0xDFFF)
                    c = 0x4E00; // CJK character
                chars[i] = (char)c;
            }
            else
            {
                // Simple emoji/symbol from BMP
                chars[i] = (char)rng.Next(0x2600, 0x2700); // Misc symbols
            }
        }
        return new string(chars);
    }
}
