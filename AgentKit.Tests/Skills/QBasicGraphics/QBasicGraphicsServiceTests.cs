using AgentKit.Skills.QBasicGraphics;
using FluentAssertions;

namespace AgentKit.Tests.Skills.QBasicGraphics;

/// <summary>
/// Unit tests for <see cref="QBasicGraphicsService"/>: the slash-command shell around
/// <see cref="QBasicGraphicsReference"/>. Focus is on the parts the tool loop depends on —
/// command detection in a chatty LLM reply, and the guarantee that a lookup never comes back as a
/// failure the model has to recover from.
/// </summary>
public class QBasicGraphicsServiceTests
{
    private readonly QBasicGraphicsService _sut = new();

    [Theory]
    [InlineData("/qbasic-grafik sprites", true)]
    [InlineData("/qbasic-grafik", true)]
    [InlineData("  /QBasic-Grafik palett", true)]
    [InlineData("/qbasic-grafiker", false)]   // longer command that merely starts the same way
    [InlineData("/qb64 spel.bas", false)]
    [InlineData("/lista", false)]
    [InlineData("", false)]
    public void IsCommand_RecognisesOnlyItsOwnCommand(string input, bool expected)
    {
        _sut.IsCommand(input).Should().Be(expected);
    }

    [Fact]
    public void TryFindCommand_FindsCommandOnItsOwnLineInALongerReply()
    {
        var reply = "Jag behöver kolla syntaxen först.\n/qbasic-grafik sprites\nSedan skriver jag filen.";

        _sut.TryFindCommand(reply, out var command).Should().BeTrue();
        command.Should().Be("/qbasic-grafik sprites");
    }

    [Fact]
    public void TryFindCommand_NoCommand_ReturnsFalse()
    {
        _sut.TryFindCommand("Jag ritar en cirkel med CIRCLE.", out var command).Should().BeFalse();
        command.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_KnownTopic_ReturnsTheArticleAsContext()
    {
        var result = await _sut.ExecuteAsync("/qbasic-grafik sprites");

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("sprites");
        result.InjectedContext.Should().Contain("GET").And.Contain("PUT");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTopic_SucceedsWithTheIndexInsteadOfFailing()
    {
        // Answering a miss with an error would spend a whole tool round teaching the model
        // nothing; the index tells it exactly which words work.
        var result = await _sut.ExecuteAsync("/qbasic-grafik ljudeffekter");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("ljudeffekter").And.Contain("sprites");
    }

    [Fact]
    public async Task ExecuteAsync_NoTopic_ReturnsTheIndex()
    {
        var result = await _sut.ExecuteAsync("/qbasic-grafik");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("skärmlägen").And.Contain("palett");
    }

    [Fact]
    public async Task ExecuteAsync_ForeignCommand_Fails()
    {
        var result = await _sut.ExecuteAsync("/qb64 spel.bas");

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void GetToolDescriptions_ListsCommandAndEveryTopic()
    {
        var descriptions = _sut.GetToolDescriptions();

        descriptions.Should().ContainSingle();
        var (signature, description) = descriptions.Single();
        signature.Should().StartWith("/qbasic-grafik");

        foreach (var topic in QBasicGraphicsReference.Topics)
            description.Should().Contain(topic.Key);
    }
}
