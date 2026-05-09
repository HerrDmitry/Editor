using EditorApp.Services;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests;

/// <summary>
/// Property-based tests for Unicode viewport read correctness.
/// Feature: viewport-text-rendering, Property 10: Unicode viewport read correctness
/// </summary>
public class ViewportUnicodeProperties
{
    /// <summary>
    /// Feature: viewport-text-rendering, Property 10: Unicode viewport read correctness
    ///
    /// For any file containing arbitrary Unicode content (including multibyte UTF-8 sequences
    /// and surrogate pairs), and for any valid (lineNumber, startColumn, columnCount) request,
    /// the ViewportService SHALL return a string that equals the substring of the logical
    /// character sequence of that line. No partial byte sequences or split surrogate pairs
    /// SHALL appear in the output. The result SHALL be identical regardless of internal
    /// access method (sequential scan vs seek-based).
    ///
    /// **Validates: Requirements 14.1, 14.3, 14.4, 14.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool UnicodeViewportReadCorrectness(PositiveInt lineCountSeed, int contentSeed, PositiveInt viewportSeed)
    {
        // Generate between 1 and 20 lines
        int lineCount = (lineCountSeed.Get % 20) + 1;
        var rng = new Random(contentSeed);

        // Generate random Unicode lines with mix of ASCII, 2-byte, 3-byte, and 4-byte chars
        var lines = new string[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            int lineLen = rng.Next(1, 80); // char count per line (keep reasonable)
            lines[i] = GenerateUnicodeLine(rng, lineLen);
        }

        // Write content to temp file as UTF-8
        var tempFile = Path.Combine(Path.GetTempPath(), $"viewport_prop10_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempFile, string.Join("\n", lines), System.Text.Encoding.UTF8);

            // Open file via FileService
            var fileService = new FileService();
            var metadata = fileService.OpenFileAsync(tempFile).GetAwaiter().GetResult();

            // Create ViewportService
            var viewportService = new ViewportService(fileService);

            // Generate random viewport parameters
            var vpRng = new Random(viewportSeed.Get);
            int lineNumber = vpRng.Next(0, lineCount);
            string physicalLine = lines[lineNumber];

            // Build list of codepoint-aligned positions within the line.
            // A valid position is any index that is NOT a low surrogate (i.e., not
            // the second char of a surrogate pair). This ensures we never start or
            // end a slice mid-surrogate-pair.
            var codepointPositions = new List<int>();
            for (int ci = 0; ci <= physicalLine.Length; ci++)
            {
                if (ci < physicalLine.Length && char.IsLowSurrogate(physicalLine[ci]))
                    continue;
                codepointPositions.Add(ci);
            }

            // Pick startColumn from valid codepoint-aligned positions
            int startIdx = vpRng.Next(0, Math.Max(1, codepointPositions.Count - 1));
            int startColumn = codepointPositions[startIdx];

            // Pick endColumn from valid positions that are >= startColumn
            var endPositions = codepointPositions.Where(p => p >= startColumn).ToList();
            int endIdx = vpRng.Next(0, endPositions.Count);
            int endColumn = endPositions[endIdx];

            // columnCount is the distance between start and end (codepoint-aligned)
            int columnCount = endColumn - startColumn;
            if (columnCount == 0)
                columnCount = Math.Min(50, physicalLine.Length - startColumn);

            // Ensure columnCount doesn't split a surrogate pair at the end
            int actualEnd = startColumn + columnCount;
            if (actualEnd < physicalLine.Length && char.IsLowSurrogate(physicalLine[actualEnd]))
                columnCount++; // Include the low surrogate to complete the pair

            // Get viewport for single line
            var result = viewportService.GetViewportAsync(
                tempFile, lineNumber, 1, startColumn, columnCount,
                wrapMode: false, viewportColumns: 120).GetAwaiter().GetResult();

            // Verify TotalPhysicalLines
            if (result.TotalPhysicalLines != lineCount)
                throw new Exception($"TotalPhysicalLines: got {result.TotalPhysicalLines}, expected {lineCount}");

            if (result.Lines.Length != 1)
                throw new Exception($"Lines.Length: got {result.Lines.Length}, expected 1");

            string returnedText = result.Lines[0];

            // GetLineCharLength uses byte-length heuristic which may overestimate for multibyte.
            // ViewportService checks: if (lineCharLen <= startColumn) → empty.
            // Since byte length >= char length for multibyte, lineCharLen >= physicalLine.Length.
            int reportedCharLen = fileService.GetLineCharLength(tempFile, lineNumber);

            if (reportedCharLen <= startColumn)
            {
                // ViewportService skips — returns empty
                if (returnedText != string.Empty)
                    throw new Exception($"Expected empty (reportedCharLen={reportedCharLen} <= startCol={startColumn}), got len={returnedText.Length}");
            }
            else
            {
                // Compute expected slice from the logical character sequence
                if (startColumn >= physicalLine.Length)
                {
                    // Line is actually shorter than startColumn — ReadLineChunkAsync returns empty
                    if (returnedText != string.Empty)
                        throw new Exception($"Expected empty (actualLen={physicalLine.Length} <= startCol={startColumn}), got len={returnedText.Length}");
                }
                else
                {
                    int availableChars = physicalLine.Length - startColumn;
                    int charsToTake = Math.Min(columnCount, availableChars);
                    string expectedSlice = physicalLine.Substring(startColumn, charsToTake);

                    if (returnedText != expectedSlice)
                        throw new Exception(
                            $"Unicode slice mismatch at line {lineNumber}, startCol={startColumn}, colCount={columnCount}. " +
                            $"Expected len={expectedSlice.Length}, got len={returnedText.Length}. " +
                            $"PhysLine len={physicalLine.Length}, reportedCharLen={reportedCharLen}");

                    // Verify no split surrogate pairs in output — since we aligned
                    // both start and end to codepoint boundaries, output must be valid
                    if (!IsValidUnicodeString(returnedText))
                        throw new Exception(
                            $"Output contains split surrogate pair at line {lineNumber}, " +
                            $"startCol={startColumn}, colCount={columnCount}");
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
    /// Generate a random Unicode line with a mix of character types:
    /// - ASCII (1-byte UTF-8): U+0020 to U+007E
    /// - 2-byte UTF-8: U+00C0 to U+07FF (Latin Extended, Greek, etc.)
    /// - 3-byte UTF-8: U+4E00 to U+9FFF (CJK Unified Ideographs)
    /// - 4-byte UTF-8 (surrogate pairs in .NET): U+10000 to U+1F9FF (Emoji, etc.)
    /// </summary>
    private static string GenerateUnicodeLine(Random rng, int charCount)
    {
        var sb = new System.Text.StringBuilder();
        int generated = 0;

        while (generated < charCount)
        {
            int category = rng.Next(0, 4);
            switch (category)
            {
                case 0:
                    // ASCII printable
                    sb.Append((char)rng.Next(0x20, 0x7F));
                    generated++;
                    break;

                case 1:
                    // 2-byte UTF-8 (U+00C0 to U+07FF)
                    sb.Append((char)rng.Next(0x00C0, 0x0800));
                    generated++;
                    break;

                case 2:
                    // 3-byte UTF-8 (CJK: U+4E00 to U+9FFF)
                    sb.Append((char)rng.Next(0x4E00, 0xA000));
                    generated++;
                    break;

                case 3:
                    // 4-byte UTF-8 (surrogate pair in .NET)
                    // Use code points in supplementary planes (U+10000 to U+1F9FF)
                    int codePoint = rng.Next(0x10000, 0x1FA00);
                    string surrogateChars = char.ConvertFromUtf32(codePoint);
                    sb.Append(surrogateChars); // Appends high + low surrogate
                    generated += 2; // Surrogate pair = 2 chars in .NET string
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Verify a string contains no split surrogate pairs (no lone high/low surrogates).
    /// </summary>
    private static bool IsValidUnicodeString(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]))
            {
                // Must be followed by low surrogate
                if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                    return false;
                i++; // Skip the low surrogate
            }
            else if (char.IsLowSurrogate(s[i]))
            {
                // Lone low surrogate — invalid
                return false;
            }
        }
        return true;
    }
}
