using EditorApp.Services;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests;

/// <summary>
/// Property-based tests for viewport slicing correctness.
/// Feature: viewport-text-rendering, Property 1: Viewport slicing returns correct rectangular region
/// </summary>
public class ViewportSlicingProperties
{
    /// <summary>
    /// Feature: viewport-text-rendering, Property 1: Viewport slicing returns correct rectangular region
    ///
    /// For any random ASCII file content (multiple lines with varying lengths) and any valid
    /// viewport rect (startLine, lineCount, startColumn, columnCount), the ViewportService
    /// SHALL return an array where each element equals the expected substring of the
    /// corresponding physical line. Lines where GetLineCharLength &lt;= startColumn return
    /// empty string. TotalPhysicalLines matches actual line count. LineLengths match
    /// GetLineCharLength values.
    ///
    /// **Validates: Requirements 1.1, 1.4, 1.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ViewportSlicingReturnsCorrectRectangularRegion(PositiveInt lineCountSeed, int contentSeed, PositiveInt viewportSeed)
    {
        // Generate between 1 and 50 lines
        int lineCount = (lineCountSeed.Get % 50) + 1;
        var rng = new Random(contentSeed);

        // Generate random ASCII lines with varying lengths (0 to 200 chars)
        // Ensure last line is non-empty to avoid trailing-newline ambiguity
        // (FileService removes trailing empty line when file ends with \n)
        var lines = new string[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            int lineLen = rng.Next(0, 201);
            // Last line must be non-empty to avoid trailing \n being interpreted
            // as "file ends with newline, no extra empty line"
            if (i == lineCount - 1 && lineLen == 0)
                lineLen = 1;
            lines[i] = GenerateAsciiLine(rng, lineLen);
        }

        // Write content to temp file with \n line endings
        var tempFile = Path.Combine(Path.GetTempPath(), $"viewport_prop1_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, string.Join("\n", lines));

            // Open file via FileService
            var fileService = new FileService();
            var metadata = fileService.OpenFileAsync(tempFile).GetAwaiter().GetResult();

            // Create ViewportService
            var viewportService = new ViewportService(fileService);

            // Generate random viewport rect from seed
            var vpRng = new Random(viewportSeed.Get);
            int startLine = vpRng.Next(0, lineCount);
            int requestLineCount = vpRng.Next(1, Math.Min(30, lineCount - startLine + 1));
            int startColumn = vpRng.Next(0, 100);
            int columnCount = vpRng.Next(1, 150);

            // Get viewport (no-wrap mode)
            var result = viewportService.GetViewportAsync(
                tempFile, startLine, requestLineCount, startColumn, columnCount,
                wrapMode: false, viewportColumns: 120).GetAwaiter().GetResult();

            // Verify TotalPhysicalLines matches actual line count
            if (result.TotalPhysicalLines != lineCount)
                throw new Exception($"TotalPhysicalLines: got {result.TotalPhysicalLines}, expected {lineCount}");

            // Compute expected number of lines returned (clamped)
            int expectedReturnedLines = Math.Min(requestLineCount, lineCount - startLine);
            if (result.Lines.Length != expectedReturnedLines)
                throw new Exception($"Lines.Length: got {result.Lines.Length}, expected {expectedReturnedLines}");

            if (result.LineLengths.Length != expectedReturnedLines)
                throw new Exception($"LineLengths.Length: got {result.LineLengths.Length}, expected {expectedReturnedLines}");

            // Verify each line
            for (int i = 0; i < expectedReturnedLines; i++)
            {
                int physLineIdx = startLine + i;
                string physicalLine = lines[physLineIdx];

                // LineLengths should match GetLineCharLength
                int reportedCharLen = fileService.GetLineCharLength(tempFile, physLineIdx);
                if (result.LineLengths[i] != reportedCharLen)
                    throw new Exception($"LineLengths[{i}]: got {result.LineLengths[i]}, expected {reportedCharLen}");

                // Verify line content based on ViewportService behavior
                if (reportedCharLen <= startColumn)
                {
                    // ViewportService skips this line — returns empty
                    if (result.Lines[i] != string.Empty)
                        throw new Exception($"Line[{i}] should be empty (charLen={reportedCharLen} <= startCol={startColumn}), got len={result.Lines[i].Length}");
                }
                else
                {
                    // ViewportService reads from file via ReadLineChunkAsync
                    if (physicalLine.Length <= startColumn)
                    {
                        // Line is actually shorter than startColumn
                        if (result.Lines[i] != string.Empty)
                            throw new Exception($"Line[{i}] should be empty (actualLen={physicalLine.Length} <= startCol={startColumn}), got len={result.Lines[i].Length}");
                    }
                    else
                    {
                        int availableChars = physicalLine.Length - startColumn;
                        int charsToTake = Math.Min(columnCount, availableChars);
                        string expectedSlice = physicalLine.Substring(startColumn, charsToTake);
                        if (result.Lines[i] != expectedSlice)
                            throw new Exception($"Line[{i}] content mismatch: physLine={physLineIdx}, actualLen={physicalLine.Length}, charLen={reportedCharLen}, startCol={startColumn}, colCount={columnCount}, got len={result.Lines[i].Length}, expected len={expectedSlice.Length}");
                    }
                }
            }

            return true;
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Generate a random ASCII-only line (printable characters 0x20-0x7E).
    /// </summary>
    private static string GenerateAsciiLine(Random rng, int length)
    {
        if (length == 0)
            return string.Empty;

        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            // ASCII printable range (space through tilde)
            chars[i] = (char)rng.Next(0x20, 0x7F);
        }
        return new string(chars);
    }
}
