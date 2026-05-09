using EditorApp.Services;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests;

/// <summary>
/// Property-based tests for virtual line count and mapping.
/// Feature: viewport-text-rendering
/// </summary>
public class ViewportVirtualLineProperties
{
    /// <summary>
    /// Feature: viewport-text-rendering, Property 3: Virtual line count formula
    ///
    /// For any array of physical line character lengths and for any positive column width W,
    /// the total virtual line count SHALL equal the sum of ceil(max(1, lineLength) / W)
    /// across all physical lines. Empty lines (length 0) SHALL count as exactly one virtual line.
    ///
    /// **Validates: Requirements 3.2, 11.1, 11.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool VirtualLineCountMatchesFormula(PositiveInt lineCountSeed, int contentSeed, PositiveInt colWidthSeed)
    {
        // Generate between 1 and 100 lines
        int lineCount = (lineCountSeed.Get % 100) + 1;
        var rng = new Random(contentSeed);

        // Generate random ASCII lines (0-300 chars, last line non-empty)
        var lines = new string[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            int lineLen = rng.Next(0, 301);
            if (i == lineCount - 1 && lineLen == 0) lineLen = 1;
            lines[i] = GenerateAsciiLine(rng, lineLen);
        }

        // Column width between 10 and 200
        int columnWidth = (colWidthSeed.Get % 191) + 10;

        var tempFile = Path.Combine(Path.GetTempPath(), $"viewport_prop3_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, string.Join("\n", lines));

            var fileService = new FileService();
            fileService.OpenFileAsync(tempFile).GetAwaiter().GetResult();

            var viewportService = new ViewportService(fileService);

            // Get actual virtual line count from service
            int actualVirtualLineCount = viewportService.GetVirtualLineCount(tempFile, columnWidth);

            // Compute expected using same formula with GetLineCharLength values
            int totalLines = fileService.GetTotalLines(tempFile);
            int expectedVirtualLineCount = 0;
            for (int i = 0; i < totalLines; i++)
            {
                int charLen = fileService.GetLineCharLength(tempFile, i);
                int effectiveLen = Math.Max(1, charLen);
                expectedVirtualLineCount += (effectiveLen + columnWidth - 1) / columnWidth;
            }

            return actualVirtualLineCount == expectedVirtualLineCount;
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Feature: viewport-text-rendering, Property 4: Virtual-to-physical line mapping
    ///
    /// For any array of physical line lengths, column width W, and virtual line offset V
    /// (where 0 ≤ V &lt; totalVirtualLines), resolving V to (physicalLine, charOffset) SHALL
    /// produce the correct segment of the physical line when read via GetViewportAsync in wrap mode.
    ///
    /// **Validates: Requirements 3.1, 3.3, 9.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool VirtualToPhysicalMappingIsCorrect(PositiveInt lineCountSeed, int contentSeed, PositiveInt colWidthSeed, PositiveInt offsetSeed)
    {
        // Generate between 1 and 50 lines
        int lineCount = (lineCountSeed.Get % 50) + 1;
        var rng = new Random(contentSeed);

        // Generate random ASCII lines (1-200 chars each)
        var lines = new string[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            int lineLen = rng.Next(1, 201);
            lines[i] = GenerateAsciiLine(rng, lineLen);
        }

        // Column width between 10 and 100
        int columnWidth = (colWidthSeed.Get % 91) + 10;

        var tempFile = Path.Combine(Path.GetTempPath(), $"viewport_prop4_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, string.Join("\n", lines));

            var fileService = new FileService();
            fileService.OpenFileAsync(tempFile).GetAwaiter().GetResult();

            var viewportService = new ViewportService(fileService);

            int totalVirtualLines = viewportService.GetVirtualLineCount(tempFile, columnWidth);
            if (totalVirtualLines == 0) return true; // edge case

            // Pick random virtual offset
            int virtualOffset = offsetSeed.Get % totalVirtualLines;

            // Get viewport in wrap mode (single virtual line)
            var result = viewportService.GetViewportAsync(
                tempFile, virtualOffset, 1, 0, columnWidth,
                wrapMode: true, viewportColumns: columnWidth).GetAwaiter().GetResult();

            if (result.Lines.Length != 1)
                return false;

            // Independently resolve virtual offset → (physicalLine, charOffset)
            int totalLines2 = fileService.GetTotalLines(tempFile);
            int virtualLinesConsumed = 0;
            int expectedPhysLine = -1;
            int expectedCharOffset = -1;

            for (int i = 0; i < totalLines2; i++)
            {
                int charLen = fileService.GetLineCharLength(tempFile, i);
                int effectiveLen = Math.Max(1, charLen);
                int virtualLinesForLine = (effectiveLen + columnWidth - 1) / columnWidth;

                if (virtualLinesConsumed + virtualLinesForLine > virtualOffset)
                {
                    int segmentIndex = virtualOffset - virtualLinesConsumed;
                    expectedPhysLine = i;
                    expectedCharOffset = segmentIndex * columnWidth;
                    break;
                }
                virtualLinesConsumed += virtualLinesForLine;
            }

            if (expectedPhysLine < 0) return false;

            // Read expected slice from the actual file line
            string physLine = lines[expectedPhysLine];
            string expectedSlice;
            if (expectedCharOffset >= physLine.Length)
            {
                expectedSlice = string.Empty;
            }
            else
            {
                int available = physLine.Length - expectedCharOffset;
                int charsToTake = Math.Min(columnWidth, available);
                expectedSlice = physLine.Substring(expectedCharOffset, charsToTake);
            }

            return result.Lines[0] == expectedSlice;
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    private static string GenerateAsciiLine(Random rng, int length)
    {
        if (length == 0) return string.Empty;
        var chars = new char[length];
        for (int i = 0; i < length; i++)
            chars[i] = (char)rng.Next(0x20, 0x7F);
        return new string(chars);
    }
}
