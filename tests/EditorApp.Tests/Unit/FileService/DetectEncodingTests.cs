using System.Text;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.FileService;

public class DetectEncodingTests
{
    [Fact]
    public void NoBOM()
    {
        var path = TempFileHelper.CreateTempFileRawBytes(Encoding.UTF8.GetBytes("hello"));
        try
        {
            var encoding = EditorApp.Services.FileService.DetectEncoding(path);
            Assert.Contains("utf-8", encoding.WebName, StringComparison.OrdinalIgnoreCase);
            // No BOM → preamble should be empty
            Assert.Empty(encoding.GetPreamble());
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public void UTF8_BOM()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = Encoding.UTF8.GetBytes("hello");
        var path = TempFileHelper.CreateTempFileRawBytes(bom.Concat(content).ToArray());
        try
        {
            var encoding = EditorApp.Services.FileService.DetectEncoding(path);
            Assert.Contains("utf-8", encoding.WebName, StringComparison.OrdinalIgnoreCase);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public void UTF16LE_BOM()
    {
        var bom = new byte[] { 0xFF, 0xFE };
        var content = Encoding.Unicode.GetBytes("hello");
        var path = TempFileHelper.CreateTempFileRawBytes(bom.Concat(content).ToArray());
        try
        {
            var encoding = EditorApp.Services.FileService.DetectEncoding(path);
            Assert.Equal(Encoding.Unicode, encoding);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public void UTF16BE_BOM()
    {
        var bom = new byte[] { 0xFE, 0xFF };
        var content = Encoding.BigEndianUnicode.GetBytes("hello");
        var path = TempFileHelper.CreateTempFileRawBytes(bom.Concat(content).ToArray());
        try
        {
            var encoding = EditorApp.Services.FileService.DetectEncoding(path);
            Assert.Equal(Encoding.BigEndianUnicode, encoding);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public void UTF32LE_BOM()
    {
        var bom = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
        var content = Encoding.UTF32.GetBytes("hello");
        var path = TempFileHelper.CreateTempFileRawBytes(bom.Concat(content).ToArray());
        try
        {
            var encoding = EditorApp.Services.FileService.DetectEncoding(path);
            Assert.Equal(Encoding.UTF32, encoding);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public void UTF32BE_BOM()
    {
        var bom = new byte[] { 0x00, 0x00, 0xFE, 0xFF };
        var content = Encoding.GetEncoding("utf-32BE").GetBytes("hello");
        var path = TempFileHelper.CreateTempFileRawBytes(bom.Concat(content).ToArray());
        try
        {
            var encoding = EditorApp.Services.FileService.DetectEncoding(path);
            Assert.Equal(Encoding.GetEncoding("utf-32BE"), encoding);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public void SingleByte()
    {
        var path = TempFileHelper.CreateTempFileRawBytes(new byte[] { 0x41 });
        try
        {
            var encoding = EditorApp.Services.FileService.DetectEncoding(path);
            Assert.Contains("utf-8", encoding.WebName, StringComparison.OrdinalIgnoreCase);
        }
        finally { TempFileHelper.Cleanup(path); }
    }

    [Fact]
    public void EmptyFile()
    {
        var path = TempFileHelper.CreateTempFileRawBytes(Array.Empty<byte>());
        try
        {
            var encoding = EditorApp.Services.FileService.DetectEncoding(path);
            Assert.Contains("utf-8", encoding.WebName, StringComparison.OrdinalIgnoreCase);
        }
        finally { TempFileHelper.Cleanup(path); }
    }
}
