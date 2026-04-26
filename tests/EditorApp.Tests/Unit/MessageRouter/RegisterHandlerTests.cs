using EditorApp.Models;
using EditorApp.Services;
using EditorApp.Tests.Fixtures;

namespace EditorApp.Tests.Unit.MessageRouter;

public class RegisterHandlerTests
{
    private readonly MockPhotinoWindowMessaging _mock = new();
    private readonly Services.MessageRouter _sut;

    public RegisterHandlerTests()
    {
        _sut = new Services.MessageRouter(_mock);
    }

    [Fact]
    public void NullHandler_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => _sut.RegisterHandler<OpenFileRequest>(null!));
    }

    [Fact]
    public async Task OverwritesPreviousHandler()
    {
        var firstInvoked = false;
        var secondInvoked = false;

        _sut.RegisterHandler<OpenFileRequest>(async _ => { firstInvoked = true; });
        _sut.RegisterHandler<OpenFileRequest>(async _ => { secondInvoked = true; });

        var json = """{"type":"OpenFileRequest","payload":{},"timestamp":"2024-01-01T00:00:00Z"}""";
        await _sut.HandleMessageAsync(json);

        Assert.False(firstInvoked);
        Assert.True(secondInvoked);
    }
}
