using AgentKit.Skills.QBasicGraphics;
using AgentKit.Skills.Qb64;
using FluentAssertions;

namespace AgentKit.Tests.Skills.QBasicGraphics;

/// <summary>
/// Unit tests for <see cref="QBasicGraphicsReference"/>: the compiled-in graphics reference the
/// LLM looks syntax up in before writing a .bas file. Covers topic lookup (by key, by keyword, by
/// free-text question) and the content guarantees the rest of the QBasic support depends on — most
/// importantly that no article teaches syntax <see cref="QBasicStructureLinter"/> would reject.
/// </summary>
public class QBasicGraphicsReferenceTests
{
    [Fact]
    public void Topics_AreNonEmptyAndUniquelyKeyed()
    {
        QBasicGraphicsReference.Topics.Should().NotBeEmpty();
        QBasicGraphicsReference.Topics.Select(t => t.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Topics_AllHaveSummaryAndBody()
    {
        foreach (var topic in QBasicGraphicsReference.Topics)
        {
            topic.Summary.Should().NotBeNullOrWhiteSpace();
            topic.Body.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Theory]
    [InlineData("skärmlägen")]
    [InlineData("punkter")]
    [InlineData("former")]
    [InlineData("data")]
    [InlineData("sprites")]
    [InlineData("sidor")]
    [InlineData("maskning")]
    [InlineData("palett")]
    [InlineData("fart")]
    [InlineData("qb64")]
    public void Find_ExactKey_ReturnsThatTopic(string key)
    {
        QBasicGraphicsReference.Find(key)!.Key.Should().Be(key);
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndTolerantOfMissingDiacritics()
    {
        // A local model that drops the dots (or a client that mangles the encoding) still lands on
        // the right article rather than getting the index back.
        QBasicGraphicsReference.Find("SKÄRMLÄGEN")!.Key.Should().Be("skärmlägen");
        QBasicGraphicsReference.Find("skarmlagen")!.Key.Should().Be("skärmlägen");
    }

    [Theory]
    [InlineData("circle", "former")]
    [InlineData("PAINT", "former")]
    [InlineData("pset", "punkter")]
    [InlineData("pcopy", "sidor")]
    [InlineData("bsave", "fart")]
    [InlineData("get", "sprites")]
    public void Find_ByKeywordTheTopicDocuments_ReturnsThatTopic(string keyword, string expectedKey)
    {
        // The model usually knows *which* statement it wants and only needs the argument order,
        // so naming the keyword has to work as well as naming the topic.
        QBasicGraphicsReference.Find(keyword)!.Key.Should().Be(expectedKey);
    }

    [Theory]
    [InlineData("hur ritar jag en cirkel", "former")]
    [InlineData("hur får jag bort flimmer i animationen", "fart")]
    [InlineData("hur gör jag en genomskinlig figur", "maskning")]
    [InlineData("vilken upplösning har screen 13", "skärmlägen")]
    public void Find_FreeTextQuestion_ReturnsMostRelevantTopic(string question, string expectedKey)
    {
        QBasicGraphicsReference.Find(question)!.Key.Should().Be(expectedKey);
    }

    [Fact]
    public void Find_UnrelatedOrEmptyQuery_ReturnsNull()
    {
        QBasicGraphicsReference.Find(null).Should().BeNull();
        QBasicGraphicsReference.Find("").Should().BeNull();
        QBasicGraphicsReference.Find("   ").Should().BeNull();
        QBasicGraphicsReference.Find("kaffebryggare").Should().BeNull();
    }

    [Fact]
    public void Find_ShortAliasInsideALongerWord_DoesNotMatch()
    {
        // "or" is a masking action verb and "data" is a topic key; substring matching would let
        // them fire on unrelated words and hand back a confidently wrong article.
        QBasicGraphicsReference.Find("format").Should().NotBe(QBasicGraphicsReference.Find("maskning"));
        QBasicGraphicsReference.Find("databasen").Should().BeNull();
    }

    [Fact]
    public void BuildIndex_ListsEveryTopicKey()
    {
        var index = QBasicGraphicsReference.BuildIndex();

        foreach (var topic in QBasicGraphicsReference.Topics)
            index.Should().Contain(topic.Key);
    }

    [Fact]
    public void Topics_AreShortEnoughForASmallContextWindow()
    {
        // The deploy box runs a 12B model at ctx 8192, where one article shares the window with the
        // workspace snapshot, the tool list and the conversation. ~2500 characters is roughly 700
        // tokens — affordable. An article that grows past this should be split, not padded.
        foreach (var topic in QBasicGraphicsReference.Topics)
            topic.Body.Length.Should().BeLessThan(2500, $"'{topic.Key}' måste rymmas i en liten kontext");
    }

    [Fact]
    public void Topics_TeachNoSyntaxTheStructureLinterRejects()
    {
        // The reference exists to stop invented keywords; an article that itself contained one
        // (a stray _LINE in an example) would actively cause the bug it is meant to prevent.
        foreach (var topic in QBasicGraphicsReference.Topics)
        {
            var issues = QBasicStructureLinter.Analyze(topic.Body)
                .Where(issue => issue.Message.Contains("finns inte i QB64")
                             || issue.Message.Contains("fel typtecken"))
                .ToList();

            issues.Should().BeEmpty($"'{topic.Key}' får inte innehålla påhittade QB64-nyckelord");
        }
    }

    [Fact]
    public void HeadlessTopic_WarnsAboutTheConsoleOnlyAndCompileOnlyRules()
    {
        // The single most expensive mistake in this environment: a graphics program written with
        // $CONSOLE:ONLY and then verified with /qb64, which always ends in a run timeout.
        var body = QBasicGraphicsReference.Find("qb64")!.Body;

        body.Should().Contain("$CONSOLE:ONLY");
        body.Should().Contain("/qb64-kompilera");
    }
}
