using System.Text;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.FileService;

public class OpenFileAsyncTests
{
    private readonly Services.FileService _sut = new();

    [Fact]
    public async Task SingleLine_NoNewline()
    {
        var path = TempFileHelper.CreateTempFile("hello");
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal(1, meta.TotalLines);

            var result = await _sut.ReadLinesAsync(path, 0, 1);
            Assert.Equal(["hello"], result.Lines);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public async Task SingleLine_TrailingLF()
    {
        var path = TempFileHelper.CreateTempFile("hello\n");
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal(1, meta.TotalLines);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public async Task MultipleLines_LF()
    {
        var path = TempFileHelper.CreateTempFile("a\nb\nc");
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal(3, meta.TotalLines);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public async Task MultipleLines_CRLF()
    {
        var path = TempFileHelper.CreateTempFile("a\r\nb\r\nc");
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal(3, meta.TotalLines);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public async Task MultipleLines_CR()
    {
        var path = TempFileHelper.CreateTempFile("a\rb\rc");
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal(3, meta.TotalLines);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public async Task MixedLineEndings()
    {
        var path = TempFileHelper.CreateTempFile("a\nb\r\nc\rd");
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal(4, meta.TotalLines);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public async Task EmptyFile()
    {
        var path = TempFileHelper.CreateTempFile("");
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal(1, meta.TotalLines);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public async Task OnlyNewlines()
    {
        var path = TempFileHelper.CreateTempFile("\n\n\n");
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal(3, meta.TotalLines);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public async Task FileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _sut.OpenFileAsync("/nonexistent/path/file.txt"));
    }

    [Fact]
    public async Task UTF8_WithBOM()
    {
        var path = TempFileHelper.CreateTempFile("hello", new UTF8Encoding(true));
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal("UTF-8", meta.Encoding);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public async Task UTF16LE_WithBOM()
    {
        var path = TempFileHelper.CreateTempFile("hello", Encoding.Unicode);
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal("UTF-16 LE", meta.Encoding);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public async Task NoBOM_DefaultsToUTF8()
    {
        var path = TempFileHelper.CreateTempFile("hello", new UTF8Encoding(false));
        try
        {
            var meta = await _sut.OpenFileAsync(path);
            Assert.Equal("UTF-8", meta.Encoding);
        }
        finally { TempFileHelper.Cleanup(path); }
    }
}
