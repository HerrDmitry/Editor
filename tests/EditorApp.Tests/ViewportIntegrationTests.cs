using System.Text;
using EditorApp.Services;

namespace EditorApp.Tests;

/// <summary>
/// Integration tests for the viewport text rendering system.
/// Feature: viewport-text-rendering
/// </summary>
public class ViewportIntegrationTests
{
    /// <summary>
    /// Full round-trip: open file → RequestViewport → verify content matches file.
    /// </summary>
    [Fact]
    public async Task OpenFile_RequestViewport_ContentMatchesFile()
    {
        // Arrange
        var lines = new[] { "Hello World", "Second line here", "Third line", "", "Fifth line" };
        var tempFile = Path.Combine(Path.GetTempPath(), $"viewport_int1_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, string.Join("\n", lines));

        try
        {
            var fileService = new FileService();
            await fileService.OpenFileAsync(tempFile);
            var viewportService = new ViewportService(fileService);

            // Act — request first 3 lines, full width
            var result = await viewportService.GetViewportAsync(
                tempFile, startLine: 0, lineCount: 3, startColumn: 0, columnCount: 200,
                wrapMode: false, viewportColumns: 120);

            // Assert
            Assert.Equal(5, result.TotalPhysicalLines);
            Assert.Equal(3, result.Lines.Length);
            Assert.Equal("Hello World", result.Lines[0]);
            Assert.Equal("Second line here", result.Lines[1]);
            Assert.Equal("Third line", result.Lines[2]);
            Assert.False(result.Truncated);
            Assert.Null(result.TotalVirtualLines);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Wrap mode: request virtual line → verify correct physical segment.
    /// </summary>
    [Fact]
    public async Task WrapMode_VirtualLine_ReturnsCorrectPhysicalSegment()
    {
        // Arrange — line 0 is 30 chars, with columnWidth=10 → 3 virtual lines
        var lines = new[] { "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234", "Short" };
        var tempFile = Path.Combine(Path.GetTempPath(), $"viewport_int2_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, string.Join("\n", lines));

        try
        {
            var fileService = new FileService();
            await fileService.OpenFileAsync(tempFile);
            var viewportService = new ViewportService(fileService);

            // Virtual line 0 → first 10 chars of line 0
            var result0 = await viewportService.GetViewportAsync(
                tempFile, startLine: 0, lineCount: 1, startColumn: 0, columnCount: 10,
                wrapMode: true, viewportColumns: 10);
            Assert.Equal("ABCDEFGHIJ", result0.Lines[0]);

            // Virtual line 1 → chars 10-19 of line 0
            var result1 = await viewportService.GetViewportAsync(
                tempFile, startLine: 1, lineCount: 1, startColumn: 0, columnCount: 10,
                wrapMode: true, viewportColumns: 10);
            Assert.Equal("KLMNOPQRST", result1.Lines[0]);

            // Virtual line 2 → chars 20-29 of line 0
            var result2 = await viewportService.GetViewportAsync(
                tempFile, startLine: 2, lineCount: 1, startColumn: 0, columnCount: 10,
                wrapMode: true, viewportColumns: 10);
            Assert.Equal("UVWXYZ1234", result2.Lines[0]);

            // Virtual line 3 → line 1 ("Short")
            var result3 = await viewportService.GetViewportAsync(
                tempFile, startLine: 3, lineCount: 1, startColumn: 0, columnCount: 10,
                wrapMode: true, viewportColumns: 10);
            Assert.Equal("Short", result3.Lines[0]);

            // Total virtual lines = 3 (line 0) + 1 (line 1) = 4
            Assert.Equal(4, result0.TotalVirtualLines);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// File refresh: verify maxLineLength updates in next response.
    /// </summary>
    [Fact]
    public async Task FileRefresh_MaxLineLengthUpdates()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"viewport_int3_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "Short\nMedium line");

        try
        {
            var fileService = new FileService();
            var meta1 = await fileService.OpenFileAsync(tempFile);
            var viewportService = new ViewportService(fileService);

            int maxLen1 = viewportService.GetMaxLineLength(tempFile);

            // Modify file — add a much longer line
            await Task.Delay(50); // ensure different timestamp
            File.WriteAllText(tempFile, "Short\nMedium line\nThis is a much longer line that exceeds the previous maximum");

            // Refresh
            await fileService.RefreshFileAsync(tempFile);

            int maxLen2 = viewportService.GetMaxLineLength(tempFile);

            // MaxLineLength should have increased
            Assert.True(maxLen2 > maxLen1, $"Expected maxLen2 ({maxLen2}) > maxLen1 ({maxLen1})");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// No-wrap mode: startColumn beyond line length returns empty string.
    /// </summary>
    [Fact]
    public async Task NoWrap_StartColumnBeyondLineLength_ReturnsEmpty()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"viewport_int4_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "Hello\nWorld");

        try
        {
            var fileService = new FileService();
            await fileService.OpenFileAsync(tempFile);
            var viewportService = new ViewportService(fileService);

            var result = await viewportService.GetViewportAsync(
                tempFile, startLine: 0, lineCount: 2, startColumn: 1000, columnCount: 50,
                wrapMode: false, viewportColumns: 120);

            // Both lines shorter than startColumn=1000 → empty
            Assert.Equal(2, result.Lines.Length);
            Assert.Equal(string.Empty, result.Lines[0]);
            Assert.Equal(string.Empty, result.Lines[1]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    /// <summary>
    /// MaxLineLength included in FileOpenMetadata after open.
    /// </summary>
    [Fact]
    public async Task FileOpen_MaxLineLengthInMetadata()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"viewport_int5_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, "Short\nA longer line with more characters\nTiny");

        try
        {
            var fileService = new FileService();
            var metadata = await fileService.OpenFileAsync(tempFile);

            // MaxLineLength should be >= longest line ("A longer line with more characters" = 35 chars)
            Assert.True(metadata.MaxLineLength >= 35,
                $"Expected MaxLineLength >= 35, got {metadata.MaxLineLength}");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
