using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.Integration;

public class MessageRoundTripTests
{
    private readonly MockPhotinoWindowMessaging _mock = new();
    private readonly Services.MessageRouter _sut;

    public MessageRoundTripTests()
    {
        _sut = new Services.MessageRouter(_mock);
    }

    [Fact]
    public async Task RoundTrip_FileOpenedResponse()
    {
        var original = new FileOpenedResponse
        {
            FileName = "test.txt",
            TotalLines = 42,
            FileSizeBytes = 1024,
            Encoding = "UTF-8"
        };

        // Send to UI — captures JSON in mock
        await _sut.SendToUIAsync(original);
        var json = _mock.SentMessages[0];

        // Register handler and feed the captured JSON back
        FileOpenedResponse? received = null;
        _sut.RegisterHandler<FileOpenedResponse>(async msg => { received = msg; });
        await _sut.HandleMessageAsync(json);

        Assert.NotNull(received);
        Assert.Equal(original.FileName, received!.FileName);
        Assert.Equal(original.TotalLines, received.TotalLines);
        Assert.Equal(original.FileSizeBytes, received.FileSizeBytes);
        Assert.Equal(original.Encoding, received.Encoding);
    }

    [Fact]
    public async Task RoundTrip_LinesResponse()
    {
        var original = new LinesResponse
        {
            StartLine = 10,
            Lines = new[] { "alpha", "beta", "gamma" },
            TotalLines = 100
        };

        await _sut.SendToUIAsync(original);
        var json = _mock.SentMessages[0];

        LinesResponse? received = null;
        _sut.RegisterHandler<LinesResponse>(async msg => { received = msg; });
        await _sut.HandleMessageAsync(json);

        Assert.NotNull(received);
        Assert.Equal(original.StartLine, received!.StartLine);
        Assert.Equal(original.Lines, received.Lines);
        Assert.Equal(original.TotalLines, received.TotalLines);
    }

    [Fact]
    public async Task RoundTrip_ErrorResponse()
    {
        var original = new ErrorResponse
        {
            ErrorCode = ErrorCode.FILE_NOT_FOUND.ToString(),
            Message = "File not found",
            Details = "The file /tmp/missing.txt does not exist."
        };

        await _sut.SendToUIAsync(original);
        var json = _mock.SentMessages[0];

        ErrorResponse? received = null;
        _sut.RegisterHandler<ErrorResponse>(async msg => { received = msg; });
        await _sut.HandleMessageAsync(json);

        Assert.NotNull(received);
        Assert.Equal(original.ErrorCode, received!.ErrorCode);
        Assert.Equal(original.Message, received.Message);
        Assert.Equal(original.Details, received.Details);
    }
}
