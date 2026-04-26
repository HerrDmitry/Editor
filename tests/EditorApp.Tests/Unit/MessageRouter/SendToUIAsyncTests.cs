using System.Text.Json;
using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.MessageRouter;

public class SendToUIAsyncTests
{
    private readonly MockPhotinoWindowMessaging _mock = new();
    private readonly Services.MessageRouter _sut;

    public SendToUIAsyncTests()
    {
        _sut = new Services.MessageRouter(_mock);
    }

    [Fact]
    public async Task SendsValidEnvelope()
    {
        var msg = new FileOpenedResponse
        {
            FileName = "test.txt",
            TotalLines = 42,
            FileSizeBytes = 1024,
            Encoding = "UTF-8"
        };

        await _sut.SendToUIAsync(msg);

        Assert.Single(_mock.SentMessages);
        using var doc = JsonDocument.Parse(_mock.SentMessages[0]);
        var root = doc.RootElement;

        Assert.Equal("FileOpenedResponse", root.GetProperty("type").GetString());
        Assert.True(root.TryGetProperty("payload", out _));
        // Verify ISO 8601 timestamp
        var timestamp = root.GetProperty("timestamp").GetString()!;
        Assert.True(DateTimeOffset.TryParse(timestamp, out _), "Timestamp should be ISO 8601");
    }

    [Fact]
    public async Task SendsLinesResponse()
    {
        var msg = new LinesResponse
        {
            StartLine = 0,
            Lines = new[] { "line1", "line2", "line3" },
            TotalLines = 100
        };

        await _sut.SendToUIAsync(msg);

        Assert.Single(_mock.SentMessages);
        using var doc = JsonDocument.Parse(_mock.SentMessages[0]);
        var root = doc.RootElement;

        Assert.Equal("LinesResponse", root.GetProperty("type").GetString());
        var payload = root.GetProperty("payload");
        Assert.True(payload.TryGetProperty("lines", out var lines));
        Assert.Equal(JsonValueKind.Array, lines.ValueKind);
        Assert.Equal(3, lines.GetArrayLength());
    }

    [Fact]
    public async Task SendsErrorResponse()
    {
        var msg = new ErrorResponse
        {
            ErrorCode = ErrorCode.FILE_NOT_FOUND.ToString(),
            Message = "File not found"
        };

        await _sut.SendToUIAsync(msg);

        Assert.Single(_mock.SentMessages);
        using var doc = JsonDocument.Parse(_mock.SentMessages[0]);
        var root = doc.RootElement;

        Assert.Equal("ErrorResponse", root.GetProperty("type").GetString());
        var payload = root.GetProperty("payload");
        Assert.True(payload.TryGetProperty("errorCode", out _));
    }

    [Fact]
    public async Task NullMessage_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.SendToUIAsync<FileOpenedResponse>(null!));
    }

    [Fact]
    public async Task PayloadCamelCase()
    {
        var msg = new FileOpenedResponse
        {
            FileName = "test.txt",
            TotalLines = 10,
            FileSizeBytes = 512,
            Encoding = "UTF-8"
        };

        await _sut.SendToUIAsync(msg);

        using var doc = JsonDocument.Parse(_mock.SentMessages[0]);
        var payload = doc.RootElement.GetProperty("payload");

        // Verify camelCase property names
        Assert.True(payload.TryGetProperty("fileName", out _));
        Assert.True(payload.TryGetProperty("totalLines", out _));
        Assert.True(payload.TryGetProperty("fileSizeBytes", out _));
        Assert.True(payload.TryGetProperty("encoding", out _));
    }
}
