using iTerminal.Core.Model.Web;

namespace iTerminal.Core.Test.Model.Web;

[TestClass]
public class HtmlLayoutTest
{
    private static WebPage Layout(string html, int width = 40) =>
        new HtmlLayout(WebDocument.Parse("https://example.test/dir/page.html", html)).Layout(width);

    private static string[] Text(WebPage page) => page.Lines.Select(l => l.Text.TrimEnd()).ToArray();

    [TestMethod]
    public void ParserClosesWhatThePageForgotToClose()
    {
        var root = HtmlParser.Parse("<ul><li>one<li>two<li>three</ul><p>a<p>b");

        var list = root.Children[0];
        Assert.AreEqual("ul", list.Tag);
        Assert.AreEqual(3, list.Children.Count);
        Assert.AreEqual(2, root.Children.Count(c => c.Tag == "p"));
    }

    [TestMethod]
    public void ParserDropsScriptsAndStylesAndDecodesEntities()
    {
        var page = Layout("<style>p{color:red}</style><script>var x = '<b>no</b>';</script><p>Fish &amp; chips &lt;3 &#65;</p>");

        CollectionAssert.AreEqual(new[] { "Fish & chips <3 A" }, Text(page));
    }

    [TestMethod]
    public void ParserSurvivesGarbage()
    {
        var page = Layout("<<div <p a='unterminated>text</b></i><td>cell</tr></table><!-- open comment");

        Assert.IsNotNull(page);
    }

    [TestMethod]
    public void ParagraphsWrapAtTheWidthAndAreSeparatedByABlankLine()
    {
        var page = Layout("<p>alpha beta gamma delta</p><p>second</p>", 12);

        CollectionAssert.AreEqual(new[] { "alpha beta", "gamma delta", "", "second" }, Text(page));
    }

    [TestMethod]
    public void WhitespaceCollapsesAndBreaksStart()
    {
        var page = Layout("<p>  one \n\n  two<br>three<br><br>four </p>");

        CollectionAssert.AreEqual(new[] { "one two", "three", "", "four" }, Text(page));
    }

    [TestMethod]
    public void AWordLongerThanTheLineIsCutRatherThanLost()
    {
        var page = Layout("<p>abcdefghijklmnop</p>", 10);

        CollectionAssert.AreEqual(new[] { "abcdefghij", "klmnop" }, Text(page));
    }

    [TestMethod]
    public void HeadingsAreUnderlined()
    {
        var page = Layout("<h1>Title</h1><p>body</p>");

        CollectionAssert.AreEqual(new[] { "Title", "═════", "", "body" }, Text(page));
        Assert.IsTrue(page.Lines[0].Spans[0].Style.HasFlag(WebStyle.Heading | WebStyle.Bold));
    }

    [TestMethod]
    public void ListsAreBulletedNumberedAndNested()
    {
        var page = Layout("<ul><li>one<ul><li>inner</li></ul></li><li>two</li></ul><ol start=9><li>nine</li><li>ten</li></ol>");

        CollectionAssert.AreEqual(
            new[] { "• one", "  ◦ inner", "• two", "", " 9. nine", "10. ten" },
            Text(page));
    }

    [TestMethod]
    public void ALongListItemWrapsUnderItsText()
    {
        var page = Layout("<ul><li>one two three four five</li></ul>", 14);

        CollectionAssert.AreEqual(new[] { "• one two", "  three four", "  five" }, Text(page));
    }

    [TestMethod]
    public void BlockQuotesAreMarkedAndPreformattedTextKeepsItsLines()
    {
        var page = Layout("<blockquote>quoted</blockquote><pre>a  b\n  c</pre>");

        CollectionAssert.AreEqual(new[] { "│ quoted", "", "a  b", "  c" }, Text(page));
    }

    [TestMethod]
    public void LinksBecomeItemsInDocumentOrder()
    {
        var html = "<p><a href='/one'>first link</a> and <a href='two.html'>second</a></p><p><a href='#end'>jump</a></p><h2 id='end'>End</h2>";
        var document = WebDocument.Parse("https://example.test/dir/page.html", html);
        var page = new HtmlLayout(document).Layout(60);

        Assert.AreEqual(3, page.Items.Count);
        Assert.IsTrue(page.Items.All(i => !i.IsField));
        Assert.AreEqual(0, page.Items[0].Column);
        Assert.AreEqual("first link and ".Length, page.Items[1].Column);
        Assert.AreEqual("https://example.test/one", document.Resolve(document.Links[0].Href));
        Assert.AreEqual("https://example.test/dir/two.html", document.Resolve(document.Links[1].Href));
        Assert.IsTrue(page.Lines[0].Spans.Any(s => s.Style.HasFlag(WebStyle.Link) && s.Link == 0));
    }

    [TestMethod]
    public void AnchorsPointAtTheLineWhereTheElementStarts()
    {
        var page = Layout("<p>intro</p><h2 id='section'>Section</h2><p><a name='old'></a>tail</p>");

        Assert.IsTrue(page.Anchors.TryGetValue("section", out var line));
        Assert.IsTrue(page.Lines[line].Text.Contains("Section") || page.Lines[line + 1].Text.Contains("Section"));
        Assert.IsTrue(page.Anchors.ContainsKey("old"));
    }

    [TestMethod]
    public void ImagesShowTheirDescriptionAndHiddenContentIsLeftOut()
    {
        var page = Layout("<p>a <img src=x.png alt='A cat'> b</p><div hidden>secret</div><div style='display: none'>secret too</div><img src=pixel.gif>");

        CollectionAssert.AreEqual(new[] { "a [A cat] b" }, Text(page));
    }

    [TestMethod]
    public void TablesBecomeAlignedColumns()
    {
        var page = Layout("<table><tr><th>Name</th><th>Qty</th></tr><tr><td>apple</td><td>3</td></tr><tr><td>fig</td><td>12</td></tr></table>");

        CollectionAssert.AreEqual(new[] { "Name   Qty", "apple  3", "fig    12" }, Text(page));
    }

    [TestMethod]
    public void BorderedTablesGetSeparatorsAndRules()
    {
        var page = Layout("<table border=1><tr><td>a</td><td>b</td></tr><tr><td>c</td><td>d</td></tr></table>");

        CollectionAssert.AreEqual(new[] { "a │ b", "──┼──", "c │ d" }, Text(page));
    }

    [TestMethod]
    public void ANarrowWindowShrinksTableColumnsToFit()
    {
        var page = Layout("<table><tr><td>one two three four five six</td><td>seven eight nine ten eleven</td></tr></table>", 30);

        Assert.IsTrue(page.Lines.All(l => l.Length <= 30), string.Join("\n", Text(page)));
        Assert.IsTrue(page.Lines.Count > 1);
        var all = string.Join(" ", Text(page));
        foreach (var word in new[] { "one", "six", "seven", "eleven" })
        {
            StringAssert.Contains(all, word);
        }
    }

    [TestMethod]
    public void FormControlsReserveRoomAndBecomeItems()
    {
        var html = "<form action='/go'><input name=q size=10 value=hi> <input type=checkbox name=c checked> <select name=s><option value=1>One<option value=2 selected>Two</select> <input type=submit value=Go><input type=hidden name=h value=1></form>";
        var document = WebDocument.Parse("https://example.test/", html);
        var page = new HtmlLayout(document).Layout(80);

        Assert.AreEqual(4, page.Items.Count);
        Assert.IsTrue(page.Items.All(i => i.IsField));
        Assert.AreEqual(WebFieldKind.Text, document.Fields[0].Kind);
        Assert.AreEqual(1, document.Fields[2].SelectedIndex);
        Assert.AreEqual("[hi        ]", document.Fields[0].Render(12, false));
        Assert.AreEqual("[x]", document.Fields[1].Render(3, false));
        Assert.AreEqual("[ Go ]", document.Fields[3].Render(6, false));

        var form = document.Fields[0].Form!;
        var pairs = form.Collect(document.Fields[3]);
        Assert.AreEqual("q=hi&c=on&s=2&h=1", WebForm.Encode(pairs));
    }

    [TestMethod]
    public void LayoutIsRepeatableAtAnotherWidth()
    {
        var document = WebDocument.Parse("https://example.test/", "<table><tr><td>some words here</td><td>more words there</td></tr></table><p>after the table</p>");
        var layout = new HtmlLayout(document);

        var wide = layout.Layout(80);
        var narrow = layout.Layout(20);
        var wideAgain = layout.Layout(80);

        Assert.AreEqual(wide.Lines.Count, wideAgain.Lines.Count);
        Assert.IsTrue(narrow.Lines.Count > wide.Lines.Count);
    }
}
