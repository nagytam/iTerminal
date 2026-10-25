using iConsole.Markdown;

namespace iConsole.Test.Markdown;

[TestClass]
public class MarkdownParserTest
{
    [TestMethod]
    public void Heading_TakesItsLevelAndText()
    {
        var blocks = MarkdownParser.Parse("## Getting started");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(MarkdownBlockKind.Heading, blocks[0].Kind);
        Assert.AreEqual(2, blocks[0].Level);
        Assert.AreEqual("Getting started", Text(blocks[0]));
    }

    [TestMethod]
    public void ConsecutiveLines_FormOneParagraph()
    {
        var blocks = MarkdownParser.Parse("first line\nsecond line\n\nnext");

        Assert.AreEqual(2, blocks.Count);
        Assert.AreEqual("first line second line", Text(blocks[0]));
        Assert.AreEqual("next", Text(blocks[1]));
    }

    [TestMethod]
    public void BulletsAndNumbers_BecomeListItems_NestedByIndentation()
    {
        var blocks = MarkdownParser.Parse("- one\n  - nested\n1) first");

        Assert.AreEqual(3, blocks.Count);
        Assert.IsTrue(blocks.All(b => b.Kind == MarkdownBlockKind.ListItem));
        Assert.AreEqual("•", blocks[0].Marker);
        Assert.AreEqual(0, blocks[0].Level);
        Assert.AreEqual(1, blocks[1].Level);
        Assert.AreEqual("1.", blocks[2].Marker);
    }

    [TestMethod]
    public void Quote_IsOneBlockPerLine()
    {
        var blocks = MarkdownParser.Parse("> quoted **text**");

        Assert.AreEqual(MarkdownBlockKind.Quote, blocks[0].Kind);
        Assert.AreEqual("quoted text", Text(blocks[0]));
    }

    [TestMethod]
    public void FencedCode_KeepsEachLineAsWritten()
    {
        var blocks = MarkdownParser.Parse("```\n  var x = 1;\n  return x;\n```");

        Assert.AreEqual(2, blocks.Count);
        Assert.IsTrue(blocks.All(b => b.Kind == MarkdownBlockKind.Code));
        Assert.AreEqual("  var x = 1;", Text(blocks[0]));
        Assert.AreEqual("  return x;", Text(blocks[1]));
    }

    [TestMethod]
    public void UnclosedFence_RunsToTheEnd()
    {
        var blocks = MarkdownParser.Parse("```\nline one\nline two");

        Assert.AreEqual(2, blocks.Count);
        Assert.IsTrue(blocks.All(b => b.Kind == MarkdownBlockKind.Code));
    }

    [TestMethod]
    public void HorizontalRule_IsAnyRunOfThreeOrMoreRuleCharacters()
    {
        Assert.AreEqual(MarkdownBlockKind.Rule, MarkdownParser.Parse("---")[0].Kind);
        Assert.AreEqual(MarkdownBlockKind.Rule, MarkdownParser.Parse("* * *")[0].Kind);
        Assert.AreEqual(MarkdownBlockKind.Rule, MarkdownParser.Parse("___")[0].Kind);
    }

    [TestMethod]
    public void EmptyOrNullInput_HasNoBlocks()
    {
        Assert.AreEqual(0, MarkdownParser.Parse(string.Empty).Count);
        Assert.AreEqual(0, MarkdownParser.Parse(null).Count);
    }

    [TestMethod]
    public void Inline_Emphasis_Code_AndLinks_AreStyled()
    {
        var spans = MarkdownInline.Parse("**bold** and *it* and `code` and [site](https://example.com)");

        Assert.AreEqual(MarkdownStyle.Bold, Style(spans, "bold"));
        Assert.AreEqual(MarkdownStyle.Italic, Style(spans, "it"));
        Assert.AreEqual(MarkdownStyle.Code, Style(spans, "code"));
        Assert.AreEqual(MarkdownStyle.Link, Style(spans, "site"));
        Assert.IsFalse(spans.Any(s => s.Text.Contains("https")), "The link address is not shown.");
    }

    [TestMethod]
    public void Inline_UnclosedDelimiters_StayLiteral()
    {
        var spans = MarkdownInline.Parse("use * alone and a `stray tick");

        Assert.AreEqual("use * alone and a `stray tick", string.Concat(spans.Select(s => s.Text)));
        Assert.IsTrue(spans.All(s => s.Style == MarkdownStyle.Plain));
    }

    [TestMethod]
    public void Inline_UnderscoreInsideAWord_IsNotEmphasis()
    {
        var spans = MarkdownInline.Parse("open file_name_here now");

        Assert.AreEqual(1, spans.Count);
        Assert.AreEqual("open file_name_here now", spans[0].Text);
    }

    [TestMethod]
    public void Inline_BackslashEscapesAStar()
    {
        var spans = MarkdownInline.Parse(@"a \* b");

        Assert.AreEqual("a * b", string.Concat(spans.Select(s => s.Text)));
    }

    private static string Text(MarkdownBlock block) => string.Concat(block.Spans.Select(s => s.Text));

    private static MarkdownStyle Style(IEnumerable<MarkdownSpan> spans, string text) =>
        spans.First(s => s.Text == text).Style;
}
