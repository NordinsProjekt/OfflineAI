using Application.AI.Gemma4;
using Xunit;

namespace Application.AI.Tests.Gemma4;

/// <summary>
/// Covers <see cref="Gemma4CliService.ExtractAnswer"/>'s handling of Gemma 4's reasoning
/// channel. The regression that motivated these: an <em>unclosed</em> channel
/// (<c>&lt;|channel&gt;thought</c> with no closing <c>&lt;channel|&gt;</c>) used to leak the raw
/// special token as the "answer", which then reached the goal-agent verdict parser and produced
/// a bogus "Granskningssvaret kunde inte tolkas … Svaret började: \"&lt;|channel&gt;thought\""
/// verdict — observed repeatedly in a real agent run.
/// </summary>
public class Gemma4ResponseExtractionTests
{
    [Fact]
    public void ExtractAnswer_ClosedChannel_ReturnsTextAfterClose()
    {
        var region = "<|channel>thought\nLet me check the file.<channel|>RESULTAT: GODKÄNT<turn|>";

        Assert.Equal("RESULTAT: GODKÄNT", Gemma4CliService.ExtractAnswer(region));
    }

    [Fact]
    public void ExtractAnswer_EmptyThinkingBlock_ReturnsAnswer()
    {
        // The thinking-off normal path: opener + empty block + close, then the answer.
        var region = "<|channel>thought<channel|>RESULTAT: UNDERKÄNT - saknar kod<turn|>";

        Assert.Equal("RESULTAT: UNDERKÄNT - saknar kod", Gemma4CliService.ExtractAnswer(region));
    }

    [Fact]
    public void ExtractAnswer_DoubledOpener_StillKeysOffClose()
    {
        var region = "<|channel><|channel>thought<channel|>Klart<turn|>";

        Assert.Equal("Klart", Gemma4CliService.ExtractAnswer(region));
    }

    [Fact]
    public void ExtractAnswer_UnclosedChannel_DoesNotLeakSpecialToken()
    {
        // The exact shape from the agent log: opener emitted, generation ended before the close.
        var region = "<|channel>thought";

        // No answer was produced — but crucially the <|channel> token must not leak out.
        var answer = Gemma4CliService.ExtractAnswer(region);

        Assert.Equal(string.Empty, answer);
        Assert.DoesNotContain("<|channel>", answer);
    }

    [Fact]
    public void ExtractAnswer_UnclosedChannelWithLeadingAnswer_KeepsAnswerBeforeOpener()
    {
        var region = "RESULTAT: GODKÄNT\n<|channel>thought om nästa steg";

        Assert.Equal("RESULTAT: GODKÄNT", Gemma4CliService.ExtractAnswer(region));
    }

    [Fact]
    public void ExtractAnswer_PlainTextNoChannel_ReturnedTrimmed()
    {
        var region = "\nRESULTAT: GODKÄNT\n";

        Assert.Equal("RESULTAT: GODKÄNT", Gemma4CliService.ExtractAnswer(region));
    }

    [Fact]
    public void ExtractAnswer_ReopenedUnclosedChannelAfterAnswer_KeepsAnswer()
    {
        // Closed thought block, an answer, then a second thought channel that never closes
        // (generation cut). Cutting only on the *first* opener (the old else-branch) misses this.
        var region = "<|channel>thought<channel|>RESULTAT: GODKÄNT\n<|channel>thought och sen då";

        Assert.Equal("RESULTAT: GODKÄNT", Gemma4CliService.ExtractAnswer(region));
    }

    [Fact]
    public void ExtractAnswer_DanglingNativeToolCall_DoesNotLeakToolTokens()
    {
        // Observed in an agent run: in the plain-chat flow (no tool registry wired) the model
        // wrapped a slash command in native tool tokens and stopped at <|tool_response>. That is
        // not an answer — and the raw tokens must not reach e.g. the verdict parser.
        var region = "<|channel>thought<channel|><|tool_call>call: /lista <tool_call|><|tool_response>";

        var answer = Gemma4CliService.ExtractAnswer(region);

        Assert.Equal(string.Empty, answer);
    }

    [Fact]
    public void ExtractAnswer_AnswerAfterServicedToolExchange_ReturnsAnswerOnly()
    {
        // A finished native tool loop: the user-facing answer follows the last tool response.
        var region =
            "<|channel>thought<channel|>" +
            "<|tool_call>call:väder{plats:\"Malmö\"}<tool_call|>" +
            "<|tool_response>response:väder{result:<|\"|>12 grader<|\"|>}<tool_response|>" +
            "Det är 12 grader i Malmö.<turn|>";

        Assert.Equal("Det är 12 grader i Malmö.", Gemma4CliService.ExtractAnswer(region));
    }
}
