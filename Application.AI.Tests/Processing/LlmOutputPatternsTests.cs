using Application.AI.Processing;

namespace Application.AI.Tests.Processing;

/// <summary>
/// Unit tests for LlmOutputPatterns, primarily the detection of the per-turn stats line
/// that newer llama-cli builds print in conversation mode. That line is the only reliable
/// end-of-generation signal in that mode (no assistant markers appear, and the process
/// stays alive waiting for the next user turn).
/// </summary>
public sealed class LlmOutputPatternsTests
{
    [Theory]
    [InlineData("[ Prompt: 2201.5 t/s | Generation: 65.7 t/s ]")]
    [InlineData("[ Prompt: 12.0 t/s | Generation: 3.4 t/s ]")]
    [InlineData("   [ Prompt: 100 t/s | Generation: 50 t/s ]")] // leading whitespace
    public void IsTurnStatsLine_WithStatsLine_ReturnsTrue(string line)
    {
        Assert.True(LlmOutputPatterns.IsTurnStatsLine(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("En operativ kan endast utföra en Fight (melee) om en fiende befinner sig inom dess Engagement Range.")]
    [InlineData("<|im_start|>assistant")]
    [InlineData("[Start thinking].")]
    [InlineData("[Kill Team - Action: Shoot - Rolling Attack and Defence Dice]")]
    [InlineData("The Prompt: says Generation: is important")] // missing "t/s" and "[" prefix
    [InlineData("[ Prompt: something ]")] // missing Generation/t-s parts
    public void IsTurnStatsLine_WithNonStatsLine_ReturnsFalse(string line)
    {
        Assert.False(LlmOutputPatterns.IsTurnStatsLine(line));
    }

    // ── IsCliNoiseLine / StripCliNoise ────────────────────────────────────
    // Newer llama-cli builds print a startup banner + interactive help on stdout. When no
    // assistant marker is found in the captured output, that banner must never be returned
    // to the caller as if the model had said it.

    [Theory]
    [InlineData("Loading model... ")]
    [InlineData("▄▄ ▄▄")]
    [InlineData("██ ██  ▀▀█▄ ███▄███▄  ▀▀█▄    ▄████ ████▄ ████▄")]
    [InlineData("build      : b9878-2da668617")]
    [InlineData("model      : d:/tinyllama/gemma-4-12b-it-Q4_K_S.gguf")]
    [InlineData("ftype      : Q4_K - Small")]
    [InlineData("modalities : text")]
    [InlineData("available commands:")]
    [InlineData("  /exit or Ctrl+C     stop or exit")]
    [InlineData("  /regen              regenerate the last response")]
    [InlineData("  /read <file>        add a text file")]
    [InlineData("> \uFEFFAnswer the question directly in one paragraph.")] // echoed stdin, with BOM
    [InlineData(">")]
    public void IsCliNoiseLine_WithBannerLine_ReturnsTrue(string line)
    {
        Assert.True(LlmOutputPatterns.IsCliNoiseLine(line));
    }

    [Theory]
    [InlineData("KRAV: Filen recept.txt finns i arbetsytan.")]
    [InlineData("RESULTAT: GODKÄNT")]
    [InlineData("/skapa number.txt")] // file-agent tool command — must never be filtered
    [InlineData("/lista")]
    [InlineData("Fibonacci-sekvensen är: 0, 1, 1, 2, 3, 5, 8")]
    [InlineData("modellen svarade bra")] // starts with a banner label word but no ':' after it
    [InlineData("")]
    public void IsCliNoiseLine_WithGenuineContent_ReturnsFalse(string line)
    {
        Assert.False(LlmOutputPatterns.IsCliNoiseLine(line));
    }

    [Fact]
    public void StripCliNoise_BannerOnlyOutput_ReturnsEmpty()
    {
        const string banner = "Loading model... \n▄▄ ▄▄\n██ ██\nbuild      : b9878-2da668617\n" +
                              "model      : d:/tinyllama/gemma-4-12b-it-Q4_K_S.gguf\nftype      : Q4_K - Small\n" +
                              "modalities : text\navailable commands:\n  /exit or Ctrl+C     stop or exit\n" +
                              "  /glob <pattern>     add text files using globbing pattern\n> \uFEFFAnswer the question directly.";

        Assert.Equal(string.Empty, LlmOutputPatterns.StripCliNoise(banner));
    }

    [Fact]
    public void StripCliNoise_BannerFollowedByRealText_KeepsOnlyRealText()
    {
        const string mixed = "Loading model... \navailable commands:\n  /exit or Ctrl+C     stop or exit\n" +
                             "RESULTAT: GODKÄNT\nFilen finns och innehåller rätt sekvens.";

        Assert.Equal("RESULTAT: GODKÄNT\nFilen finns och innehåller rätt sekvens.",
            LlmOutputPatterns.StripCliNoise(mixed));
    }
}
