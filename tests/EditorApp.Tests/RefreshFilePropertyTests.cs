using System.Text;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;
using FsCheck;
using FsCheck.Xunit;

namespace EditorApp.Tests;

/// <summary>
/// Property-based tests for external file refresh correctness.
/// Feature: external-file-refresh
/// </summary>
public class RefreshFilePropertyTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
            TempFileHelper.Cleanup(path);
    }

    private string CreateTempFileWithContent(string content)
    {
        var path = TempFileHelper.CreateTempFile(content);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Build random file content: N lines with random lengths and mixed line endings.
    /// Returns (fullContent, expectedLines) where expectedLines are the logical lines
    /// that ReadLinesAsync should return.
    /// </summary>
    private static (string content, string[] lines) GenerateRandomFileContent(Random rng, int lineCount)
    {
        var lines = new string[lineCount];
        for (int i = 0; i < lineCount; i++)
        {
            var len = rng.Next(1, 120);
            var chars = new char[len];
            for (int c = 0; c < len; c++)
            {
                // Printable ASCII excluding \r and \n
                chars[c] = (char)rng.Next(32, 127);
            }
            lines[i] = new string(chars);
        }

        var sb = new StringBuilder();
        for (int i = 0; i < lineCount; i++)
        {
            sb.Append(lines[i]);
            if (i < lineCount - 1)
            {
                // Pick random line ending: \n, \r\n, or \r
                var endingType = rng.Next(3);
                sb.Append(endingType switch
                {
                    0 => "\n",
                    1 => "\r\n",
                    _ => "\r"
                });
            }
        }

        return (sb.ToString(), lines);
    }

    /// <summary>
    /// Feature: external-file-refresh, Property 3: Refresh produces correct index
    ///
    /// For any file with initial content C1 and modified content C2, after RefreshFileAsync
    /// completes, the cached CompressedLineIndex SHALL have LineCount equal to the number
    /// of lines in C2, and ReadLinesAsync(path, 0, lineCount) SHALL return the lines of C2.
    ///
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RefreshProducesCorrectIndexMatchingModifiedFile(
        PositiveInt initialLinesSeed,
        PositiveInt modifiedLinesSeed,
        int initialContentSeed,
        int modifiedContentSeed)
    {
        // Generate initial content: 1-100 lines
        var initialLineCount = (initialLinesSeed.Get % 100) + 1;
        var rng1 = new Random(initialContentSeed);
        var (initialContent, _) = GenerateRandomFileContent(rng1, initialLineCount);

        // Generate modified content: 1-100 lines (different seed → different content)
        var modifiedLineCount = (modifiedLinesSeed.Get % 100) + 1;
        var rng2 = new Random(modifiedContentSeed);
        var (modifiedContent, expectedModifiedLines) = GenerateRandomFileContent(rng2, modifiedLineCount);

        // Create temp file with initial content and open it
        var path = CreateTempFileWithContent(initialContent);
        var sut = new FileService();

        var initialMetadata = sut.OpenFileAsync(path).GetAwaiter().GetResult();

        // Verify initial open worked
        if (initialMetadata.TotalLines != initialLineCount)
            return false;

        // Modify file with new content (simulating external process)
        var enc = new UTF8Encoding(false);
        File.WriteAllBytes(path, enc.GetBytes(modifiedContent));

        // Refresh the file
        var refreshedMetadata = sut.RefreshFileAsync(path).GetAwaiter().GetResult();

        // Verify: LineCount matches modified content
        if (refreshedMetadata.TotalLines != modifiedLineCount)
            return false;

        // Verify: ReadLinesAsync returns lines of modified content
        var result = sut.ReadLinesAsync(path, 0, modifiedLineCount).GetAwaiter().GetResult();

        if (result.Lines.Length != modifiedLineCount)
            return false;

        if (result.TotalLines != modifiedLineCount)
            return false;

        for (int i = 0; i < modifiedLineCount; i++)
        {
            if (result.Lines[i] != expectedModifiedLines[i])
                return false;
        }

        return true;
    }
}
