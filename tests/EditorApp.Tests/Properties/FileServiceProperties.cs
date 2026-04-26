using System.Text;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests.Properties;

/// <summary>
/// Property-based tests for FileService line index and reading.
/// **Validates: Requirements 9.1, 9.2, 9.4**
/// </summary>
public class FileServiceProperties
{
    /// <summary>
    /// Helper: write an array of lines to a temp file (LF-separated, no trailing newline).
    /// </summary>
    private static string WriteLinesToTempFile(string[] lines)
    {
        var content = string.Join("\n", lines);
        return TempFileHelper.CreateTempFile(content);
    }

    /// <summary>
    /// Filter: lines must not contain \r or \n (we test the index, not line-ending parsing).
    /// Also filter out null strings and surrogate chars.
    /// Lines must not be empty to avoid trailing-newline ambiguity.
    /// </summary>
    private static bool IsValidLineArray(string[] lines)
    {
        if (lines == null || lines.Length == 0 || lines.Length > 100)
            return false;
        foreach (var line in lines)
        {
            if (line == null || line.Length == 0) return false;
            foreach (var c in line)
            {
                if (c == '\r' || c == '\n' || char.IsSurrogate(c))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// **Validates: Requirements 9.1, 9.2**
    /// For each line N in a random array, ReadLinesAsync(N, 1)[0] == original[N].
    /// </summary>
    [Property(MaxTest = 100)]
    public bool LineIndexRoundTrip(string[] lines)
    {
        if (!IsValidLineArray(lines)) return true; // skip invalid inputs

        var path = WriteLinesToTempFile(lines);
        try
        {
            var sut = new FileService();
            sut.OpenFileAsync(path).GetAwaiter().GetResult();

            for (int i = 0; i < lines.Length; i++)
            {
                var result = sut.ReadLinesAsync(path, i, 1).GetAwaiter().GetResult();
                if (result.Lines.Length != 1 || result.Lines[0] != lines[i])
                    return false;
            }
            return true;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// **Validates: Requirements 9.1**
    /// OpenFileAsync.TotalLines == number of lines written.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool TotalLinesAccuracy(string[] lines)
    {
        if (!IsValidLineArray(lines)) return true; // skip invalid inputs

        var path = WriteLinesToTempFile(lines);
        try
        {
            var sut = new FileService();
            var meta = sut.OpenFileAsync(path).GetAwaiter().GetResult();
            return meta.TotalLines == lines.Length;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// **Validates: Requirements 9.2**
    /// ReadLinesAsync(start, count) returns the exact slice of original lines.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ReadLinesRangeCorrectness(string[] lines, int startSeed, int countSeed)
    {
        if (!IsValidLineArray(lines)) return true; // skip invalid inputs

        // Derive valid start and count from seeds
        var start = Math.Abs(startSeed % lines.Length);
        var maxCount = lines.Length - start;
        var count = maxCount > 0 ? Math.Abs(countSeed % (maxCount + 1)) : 0;

        var path = WriteLinesToTempFile(lines);
        try
        {
            var sut = new FileService();
            sut.OpenFileAsync(path).GetAwaiter().GetResult();

            var result = sut.ReadLinesAsync(path, start, count).GetAwaiter().GetResult();
            var expected = lines.Skip(start).Take(count).ToArray();

            if (result.Lines.Length != expected.Length)
                return false;

            for (int i = 0; i < expected.Length; i++)
            {
                if (result.Lines[i] != expected[i])
                    return false;
            }
            return true;
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    /// <summary>
    /// **Validates: Requirements 9.4**
    /// OpenFileAsync.FileSizeBytes matches the actual file size on disk.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FileSizeAccuracy(string[] lines)
    {
        if (!IsValidLineArray(lines)) return true; // skip invalid inputs

        var path = WriteLinesToTempFile(lines);
        try
        {
            var sut = new FileService();
            var meta = sut.OpenFileAsync(path).GetAwaiter().GetResult();
            var actualSize = new FileInfo(path).Length;
            return meta.FileSizeBytes == actualSize;
        }
        finally { TempFileHelper.Cleanup(path); }
    }
}
