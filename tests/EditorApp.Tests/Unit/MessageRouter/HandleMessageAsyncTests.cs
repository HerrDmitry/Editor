using System.Text.Json;
using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.MessageRouter;

public class HandleMessageAsyncTests
{
    private readonly MockPhotinoWindowMessaging _mock = new();
    private readonly Services.MessageRouter _sut;

    public HandleMessageAsyncTests()
    {
        _sut = new Services.MessageRouter(_mock);
    }

    [Fact]
    public async Task RoutesToRegisteredHandler()
    {
        var invoked = false;
        _sut.RegisterHandler<OpenFileRequest>(async _ => { invoked = true; });

        var json = """{"type":"OpenFileRequest","payload":{},"timestamp":"2024-01-01T00:00:00Z"}""";
        await _sut.HandleMessageAsync(json);

        Assert.True(invoked);
    }

    [Fact]
    public async Task RoutesToCorrectHandler()
    {
        var openInvoked = false;
        var linesInvoked = false;
        _sut.RegisterHandler<OpenFileRequest>(async _ => { openInvoked = true; });
        _sut.RegisterHandler<RequestLinesMessage>(async _ => { linesInvoked = true; });

        var json = """{"type":"OpenFileRequest","payload":{},"timestamp":"2024-01-01T00:00:00Z"}""";
        await _sut.HandleMessageAsync(json);

        Assert.True(openInvoked);
        Assert.False(linesInvoked);
    }

    [Fact]
    public async Task IgnoresEmptyString()
    {
        var invoked = false;
        _sut.RegisterHandler<OpenFileRequest>(async _ => { invoked = true; });

        await _sut.HandleMessageAsync("");

        Assert.False(invoked);
    }

    [Fact]
    public async Task IgnoresNonJsonString()
    {
        var invoked = false;
        _sut.RegisterHandler<OpenFileRequest>(async _ => { invoked = true; });

        await _sut.HandleMessageAsync("_blazor:init");

        Assert.False(invoked);
    }

    [Fact]
    public async Task IgnoresMalformedJson()
    {
        var invoked = false;
        _sut.RegisterHandler<OpenFileRequest>(async _ => { invoked = true; });

        await _sut.HandleMessageAsync("{broken");

        Assert.False(invoked);
    }

    [Fact]
    public async Task IgnoresUnknownType()
    {
        var invoked = false;
        _sut.RegisterHandler<OpenFileRequest>(async _ => { invoked = true; });

        var json = """{"type":"UnknownType","payload":{},"timestamp":"2024-01-01T00:00:00Z"}""";
        await _sut.HandleMessageAsync(json);

        Assert.False(invoked);
    }

    [Fact]
    public async Task IgnoresMissingType()
    {
        var invoked = false;
        _sut.RegisterHandler<OpenFileRequest>(async _ => { invoked = true; });

        var json = """{"payload":{}}""";
        await _sut.HandleMessageAsync(json);

        Assert.False(invoked);
    }

    [Fact]
    public async Task HandlerExceptionSwallowed()
    {
        _sut.RegisterHandler<OpenFileRequest>(async _ => throw new InvalidOperationException("boom"));

        var json = """{"type":"OpenFileRequest","payload":{},"timestamp":"2024-01-01T00:00:00Z"}""";

        // Should not throw
        var ex = await Record.ExceptionAsync(() => _sut.HandleMessageAsync(json));
        Assert.Null(ex);
    }

    [Fact]
    public async Task NullPayloadCreatesDefault()
    {
        OpenFileRequest? received = null;
        _sut.RegisterHandler<OpenFileRequest>(async msg => { received = msg; });

        var json = """{"type":"OpenFileRequest","payload":null,"timestamp":"2024-01-01T00:00:00Z"}""";
        await _sut.HandleMessageAsync(json);

        Assert.NotNull(received);
    }

    [Fact]
    public async Task DeserializesPayload()
    {
        RequestLinesMessage? received = null;
        _sut.RegisterHandler<RequestLinesMessage>(async msg => { received = msg; });

        var json = """{"type":"RequestLinesMessage","payload":{"startLine":5,"lineCount":10},"timestamp":"2024-01-01T00:00:00Z"}""";
        await _sut.HandleMessageAsync(json);

        Assert.NotNull(received);
        Assert.Equal(5, received!.StartLine);
        Assert.Equal(10, received.LineCount);
    }
}
