using iConsole.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Services;

/// <summary>Markup becomes readable lines: headings and list items marked, table cells barred, preformatted spacing kept, scripts dropped.</summary>
[TestClass]
public class HtmlTextExtractorTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Headings_AreMarkedWithHashes_AndParagraphsStandOnTheirOwnLines()
    {
        var lines = HtmlTextExtractor.Lines("<h2>Chapter</h2><p>First line.</p><p>Second.</p>");

        CollectionAssert.AreEqual(new[] { "## Chapter", "First line.", "Second." }, lines.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListItems_AreBulleted()
    {
        var lines = HtmlTextExtractor.Lines("<ul><li>one</li><li>two</li></ul>");

        CollectionAssert.AreEqual(new[] { "• one", "• two" }, lines.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TableCells_AreSeparatedByBars()
    {
        var lines = HtmlTextExtractor.Lines("<table><tr><td>a</td><td>b</td></tr></table>");

        StringAssert.Contains(lines[0], "│");
        StringAssert.Contains(lines[0], "a");
        StringAssert.Contains(lines[0], "b");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AScriptAndItsStyle_AreDropped()
    {
        var lines = HtmlTextExtractor.Lines("<style>p{color:red}</style><script>var x=1;</script><p>visible</p>");

        CollectionAssert.AreEqual(new[] { "visible" }, lines.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APreformattedBlock_KeepsItsIndentation()
    {
        // Collapsing whitespace used to flatten a code listing to one-space indents.
        var lines = HtmlTextExtractor.Lines("<pre>if (x)\n    return  1;</pre>");

        CollectionAssert.AreEqual(new[] { "if (x)", "    return  1;" }, lines.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OutsideAPreformattedBlock_SpacesStillCollapse()
    {
        var lines = HtmlTextExtractor.Lines("<p>one     two\t\tthree</p>");

        CollectionAssert.AreEqual(new[] { "one two three" }, lines.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Entities_AreDecoded()
    {
        var lines = HtmlTextExtractor.Lines("<p>Tom &amp; Jerry &lt;3</p>");

        CollectionAssert.AreEqual(new[] { "Tom & Jerry <3" }, lines.ToList());
    }
}
