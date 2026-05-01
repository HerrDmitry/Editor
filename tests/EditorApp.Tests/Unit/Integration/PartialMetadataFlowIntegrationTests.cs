using System.Text;
using EditorApp.Models;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.Integration;

/// <summary>
/// Integration tests for end-to-end partial metadata flow.
/// Uses REAL FileService with real temp files + MockMessageRouter to capture sent messages.
/// Validates: Requirements 1.1, 1.2, 2.1, 2.3, 3.1, 5.1, 6.1, 9.1
/// </summary>
public class PartialMetadataFlowIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            TempFileHelper.Cleanup(path);
        }
    }

    /// <summary>
    /// Creates a large temp file (>256KB) with known line content.
    /// Returns path and expected approximate line count at threshold.
    /// </summary>
    private string CreateLargeFile(int totalSizeBytes = 512_000, int avgLineLength = 80)
    {
        var sb = new StringBuilder();
        int lineNum = 0;
        while (sb.Length < totalSizeBytes)
        {
            // Each line: "Line NNNN: " + padding to reach avgLineLength
            var prefix = $"Line {lineNum:D6}: ";
            var padding = new string('X', Math.Max(0, avgLineLength - prefix.Length - 1));
            sb.AppendLine(prefix + padding);
            lineNum++;
        }

        var path = TempFileHelper.CreateTempFile(sb.ToString());
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Creates a small temp file (<256KB).
    /// </summary>
    private string CreateSmallFile(int lines = 50)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
        {
            sb.AppendLine($"Small line {i}");
        }

        var path = TempFileHelper.CreateTempFile(sb.ToString());
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Test 1: Open large file → partial response → lines readable → final response → totalLines updated.
    /// Validates: Requirements 1.1, 1.2, 2.1, 5.1, 6.1
    /// </summary>
    [Fact]
    public async Task LargeFile_PartialThenFinal_LinesReadableBetween()
    {
        // Arrange: real FileService + MockMessageRouter
        var mockRouter = new MockMessageRouter();
        var fileService = new EditorApp.Services.FileService();
        var sut = new EditorApp.Services.PhotinoHostService(mockRouter, fileService);
        var largePath = CreateLargeFile(totalSizeBytes: 512_000);

        // Act: open large file
        await sut.OpenFileByPathAsync(largePath);

        // Assert: get FileOpenedResponse messages
        var responses = mockRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .ToList();

        // Should have partial (IsPartial=true) then final (IsPartial=false)
        Assert.True(responses.Count >= 2, $"Expected at least 2 FileOpenedResponse messages, got {responses.Count}");

        var partialResponse = responses.First(r => r.IsPartial);
        var finalResponse = responses.Last(r => !r.IsPartial);

        // Partial response has IsPartial=true and provisional totalLines
        Assert.True(partialResponse.IsPartial);
        Assert.True(partialResponse.TotalLines > 0, "Partial response should have >0 lines");

        // Final response has IsPartial=false and >= partial totalLines
        Assert.False(finalResponse.IsPartial);
        Assert.True(finalResponse.TotalLines >= partialResponse.TotalLines,
            $"Final totalLines ({finalResponse.TotalLines}) should be >= partial ({partialResponse.TotalLines})");

        // Lines should be readable after final scan
        var linesResult = await fileService.ReadLinesAsync(largePath, 0, 10);
        Assert.Equal(10, linesResult.Lines.Length);
        Assert.Equal(finalResponse.TotalLines, linesResult.TotalLines);

        // Verify lines contain expected content
        Assert.StartsWith("Line 000000:", linesResult.Lines[0]);
    }

    /// <summary>
    /// Test 2: Open large file mid-scan → cancel → new file loads correctly.
    /// Validates: Requirements 9.1, 2.3
    /// </summary>
    [Fact]
    public async Task OpenLargeFileMidScan_Cancel_NewFileLoadsCorrectly()
    {
        // Arrange: real FileService + MockMessageRouter
        var mockRouter = new MockMessageRouter();
        var fileService = new EditorApp.Services.FileService();
        var sut = new EditorApp.Services.PhotinoHostService(mockRouter, fileService);

        // Create two large files
        var largePath1 = CreateLargeFile(totalSizeBytes: 1_000_000);
        var largePath2 = CreateLargeFile(totalSizeBytes: 512_000);

        // Act: open first file, then immediately open second (cancels first)
        // Since OpenFileByPathAsync is async and cancels previous scan,
        // we start first without awaiting, then open second.
        var firstTask = sut.OpenFileByPathAsync(largePath1);
        // Small delay to let first scan start
        await Task.Delay(10);
        var secondTask = sut.OpenFileByPathAsync(largePath2);

        // Wait for both to complete
        await firstTask;
        await secondTask;

        // Assert: second file's final response is present
        var finalResponses = mockRouter.SentMessages
            .Where(m => m.Message is FileOpenedResponse)
            .Select(m => (FileOpenedResponse)m.Message)
            .Where(r => !r.IsPartial)
            .ToList();

        // At least one final response for the second file
        var secondFileResponse = finalResponses.LastOrDefault(r =>
            r.FileName == Path.GetFileName(largePath2));
        Assert.NotNull(secondFileResponse);
        Assert.False(secondFileResponse!.IsPartial);
        Assert.True(secondFileResponse.TotalLines > 0);

        // No error messages should be sent for cancelled scan
        Assert.Empty(mockRouter.ErrorMessages);

        // Second file should be readable
        var linesResult = await fileService.ReadLinesAsync(largePath2, 0, 5);
        Assert.Equal(5, linesResult.Lines.Length);
        Assert.StartsWith("Line 000000:", linesResult.Lines[0]);
    }

    /// <summary>
    /// Test 3: ReadLinesAsync during scan → correct clamped results.
    /// Uses onPartialMetadata callback to call ReadLinesAsync with range beyond partial index.
    /// Validates: Requirements 3.1, 1.1
    /// </summary>
    [Fact]
    public async Task ReadLinesAsyncDuringScan_ClampedResults()
    {
        // Arrange: real FileService, but we need to intercept the partial callback
        // to call ReadLinesAsync during the scan.
        var fileService = new EditorApp.Services.FileService();
        var largePath = CreateLargeFile(totalSizeBytes: 512_000);

        LinesResult? midScanResult = null;
        int partialTotalLines = 0;

        // Use OpenFileAsync directly with a callback that reads lines mid-scan
        Action<FileOpenMetadata> onPartial = (meta) =>
        {
            partialTotalLines = meta.TotalLines;

            // Request lines beyond the partial index — should be clamped
            var beyondRange = meta.TotalLines + 1000;
            midScanResult = fileService.ReadLinesAsync(largePath, 0, beyondRange).GetAwaiter().GetResult();
        };

        // Act: open file with partial callback
        var finalMeta = await fileService.OpenFileAsync(largePath, onPartial);

        // Assert: partial callback was invoked (large file)
        Assert.True(partialTotalLines > 0, "Partial callback should have fired for large file");

        // Mid-scan ReadLinesAsync result should be clamped
        Assert.NotNull(midScanResult);
        Assert.True(midScanResult!.Lines.Length <= partialTotalLines,
            $"Lines returned ({midScanResult.Lines.Length}) should be <= partial totalLines ({partialTotalLines})");
        Assert.Equal(partialTotalLines, midScanResult.TotalLines);

        // Lines content should be valid
        Assert.True(midScanResult.Lines.Length > 0, "Should return at least some lines");
        Assert.StartsWith("Line 000000:", midScanResult.Lines[0]);

        // Final metadata should have more lines than partial
        Assert.True(finalMeta.TotalLines >= partialTotalLines,
            $"Final totalLines ({finalMeta.TotalLines}) should be >= partial ({partialTotalLines})");

        // After scan completes, full range should be available
        var postScanResult = await fileService.ReadLinesAsync(largePath, 0, finalMeta.TotalLines);
        Assert.Equal(finalMeta.TotalLines, postScanResult.TotalLines);
        Assert.Equal(finalMeta.TotalLines, postScanResult.Lines.Length);
    }
}
