using System.Text;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.FileService;

public class ReadLinesAsyncTests : IDisposable
{
    private readonly Services.FileService _sut = new();
    private readonly string _path;

    public ReadLinesAsyncTests()
    {
        // Create a 100-line file where line N contains "Line {N+1}" (1-based)
        var lines = Enumerable.Range(1, 100).Select(i => $"Line {i}");
        var content = string.Join("\n", lines);
        _path = TempFileHelper.CreateTempFile(content);
        _sut.OpenFileAsync(_path).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        TempFileHelper.Cleanup(_path);
    }

    [Fact]
    public async Task ReadFirstLines()
    {
        var result = await _sut.ReadLinesAsync(_path, 0, 5);
        Assert.Equal(["Line 1", "Line 2", "Line 3", "Line 4", "Line 5"], result.Lines);
    }

    [Fact]
    public async Task ReadMiddleLines()
    {
        var result = await _sut.ReadLinesAsync(_path, 50, 5);
        Assert.Equal(["Line 51", "Line 52", "Line 53", "Line 54", "Line 55"], result.Lines);
    }

    [Fact]
    public async Task ReadLastLines()
    {
        var result = await _sut.ReadLinesAsync(_path, 95, 10);
        Assert.Equal(5, result.Lines.Length);
        Assert.Equal(["Line 96", "Line 97", "Line 98", "Line 99", "Line 100"], result.Lines);
    }

    [Fact]
    public async Task ReadBeyondEnd()
    {
        var result = await _sut.ReadLinesAsync(_path, 200, 5);
        Assert.Empty(result.Lines);
        Assert.Equal(100, result.TotalLines);
    }

    [Fact]
    public async Task NegativeStartLine()
    {
        var result = await _sut.ReadLinesAsync(_path, -5, 5);
        Assert.Equal(["Line 1", "Line 2", "Line 3", "Line 4", "Line 5"], result.Lines);
    }

    [Fact]
    public async Task ZeroLineCount()
    {
        var result = await _sut.ReadLinesAsync(_path, 0, 0);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task FileNotOpened()
    {
        var freshService = new Services.FileService();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => freshService.ReadLinesAsync("/some/unopened/file.txt", 0, 5));
    }

    [Fact]
    public async Task ContentPreservation()
    {
        var specialContent = "\ttab\t\n  spaces  \nspecial: àéîõü";
        var specialPath = TempFileHelper.CreateTempFile(specialContent);
        try
        {
            var freshService = new Services.FileService();
            await freshService.OpenFileAsync(specialPath);
            var result = await freshService.ReadLinesAsync(specialPath, 0, 3);
            Assert.Equal("\ttab\t", result.Lines[0]);
            Assert.Equal("  spaces  ", result.Lines[1]);
            Assert.Equal("special: àéîõü", result.Lines[2]);
        }
        finally { TempFileHelper.Cleanup(specialPath); }
    }
}
