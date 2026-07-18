using FluentAssertions;
using Services.AgentTools;

namespace Services.Tests.AgentTools;

/// <summary>
/// Unit tests for <see cref="AgentToolRegistry"/>: registering tool metadata with a handler,
/// listing registered tools, and dispatching invocations by name.
/// </summary>
public class AgentToolRegistryTests
{
    private readonly AgentToolRegistry _sut = new();

    private static AgentTool CreateTool(string name = "echo") =>
        new(name, $"Echoes back its input ({name})", new Dictionary<string, ToolParameter>
        {
            ["text"] = new ToolParameter("string", "Text to echo"),
        });

    // ── Register / GetTools ──────────────────────────────────────────────

    [Fact]
    public void GetTools_NoneRegistered_ReturnsEmpty()
    {
        _sut.GetTools().Should().BeEmpty();
    }

    [Fact]
    public void Register_SingleTool_AppearsInGetTools()
    {
        var tool = CreateTool();

        _sut.Register(tool, _ => Task.FromResult("result"));

        _sut.GetTools().Should().ContainSingle().Which.Should().Be(tool);
    }

    [Fact]
    public void Register_MultipleTools_AllAppearInGetTools()
    {
        _sut.Register(CreateTool("tool_a"), _ => Task.FromResult("a"));
        _sut.Register(CreateTool("tool_b"), _ => Task.FromResult("b"));

        _sut.GetTools().Should().HaveCount(2);
        _sut.GetTools().Select(t => t.Name).Should().BeEquivalentTo("tool_a", "tool_b");
    }

    [Fact]
    public void Register_SameNameTwice_OverwritesPreviousRegistration()
    {
        _sut.Register(CreateTool("tool_a"), _ => Task.FromResult("first"));
        _sut.Register(CreateTool("tool_a"), _ => Task.FromResult("second"));

        _sut.GetTools().Should().ContainSingle();
    }

    [Fact]
    public void Register_NullTool_ThrowsArgumentNullException()
    {
        var act = () => _sut.Register(null!, _ => Task.FromResult("x"));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Register_NullHandler_ThrowsArgumentNullException()
    {
        var act = () => _sut.Register(CreateTool(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── InvokeAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_RegisteredTool_InvokesHandlerAndReturnsResult()
    {
        _sut.Register(CreateTool("echo"), args => Task.FromResult($"echo: {args["text"]}"));

        var result = await _sut.InvokeAsync("echo", new Dictionary<string, string> { ["text"] = "hej" });

        result.Should().Be("echo: hej");
    }

    [Fact]
    public async Task InvokeAsync_PassesArgumentsThroughToHandler()
    {
        IReadOnlyDictionary<string, string>? capturedArgs = null;
        _sut.Register(CreateTool("capture"), args =>
        {
            capturedArgs = args;
            return Task.FromResult("ok");
        });

        var arguments = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        await _sut.InvokeAsync("capture", arguments);

        capturedArgs.Should().ContainKey("a").WhoseValue.Should().Be("1");
        capturedArgs.Should().ContainKey("b").WhoseValue.Should().Be("2");
    }

    [Fact]
    public void InvokeAsync_ToolNameIsCaseInsensitive()
    {
        _sut.Register(CreateTool("Create_File"), _ => Task.FromResult("created"));

        var act = () => _sut.InvokeAsync("create_file", new Dictionary<string, string>());

        act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_UnknownTool_ThrowsKeyNotFoundException()
    {
        var act = async () => await _sut.InvokeAsync("does_not_exist", new Dictionary<string, string>());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
