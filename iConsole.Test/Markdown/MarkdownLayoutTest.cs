using iConsole.Markdown;

namespace iConsole.Test.Markdown;

[TestClass]
public class MarkdownLayoutTest
{
    private static List<string> Rows(string markdown, int width) =>
        MarkdownLayout.Wrap(MarkdownParser.Parse(markdown), width)
            .Select(line => string.Concat(line.Spans.Select(s => s.Text)))
            .ToList();

    [TestMethod]
    public void Paragraph_WrapsOnWordBoundaries()
    {
        Assert.AreEqual(2, Rows("alpha beta gamma delta", 11).Count);
        CollectionAssert.AreEqual(new[] { "alpha beta", "gamma delta" }, Rows("alpha beta gamma delta", 11));
    }

    [TestMethod]
    public void WordLongerThanTheRow_IsBroken()
    {
        CollectionAssert.AreEqual(new[] { "abcd", "efgh", "ij" }, Rows("abcdefghij", 4));
    }

    [TestMethod]
    public void ListItem_HasABulletAndAHangingIndent()
    {
        CollectionAssert.AreEqual(new[] { "• one two", "  three four" }, Rows("- one two three four", 12));
    }

    [TestMethod]
    public void Blocks_AreSeparatedByABlankRow()
    {
        CollectionAssert.AreEqual(new[] { "p", string.Empty, "h" }, Rows("p\n\n# h", 20));
    }

    [TestMethod]
    public void Rule_FillsTheRow()
    {
        CollectionAssert.AreEqual(new[] { "─────" }, Rows("---", 5));
    }

    [TestMethod]
    public void Code_IsCutAtTheEdgeNotWrapped()
    {
        CollectionAssert.AreEqual(new[] { "abcd" }, Rows("```\nabcdefgh\n```", 4));
    }

    [TestMethod]
    public void Quote_EachRowIsPrefixed()
    {
        CollectionAssert.AreEqual(new[] { "│ one two", "│ three" }, Rows("> one two three", 9));
    }
}
