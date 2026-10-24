using iConsole.Model;
using iConsole.Xml;
using System.Xml.Linq;

namespace iConsole.Test.Xml;

[TestClass]
public class XmlTreeTest
{
    private static XmlTree Build(string xml) => XmlTree.Build(XDocument.Parse(xml));

    [TestMethod]
    public void Elements_ShowTheirNameAndAttributes()
    {
        XmlTree tree = Build("""<catalog><book id="bk101" lang="en"><title>Dune</title></book></catalog>""");

        XmlNodeItem book = (XmlNodeItem)tree.Root.Children.AsSnapshot().Single().Children.AsSnapshot().Single();

        Assert.AreEqual("<book id=\"bk101\" lang=\"en\">", book.Name);
    }

    [TestMethod]
    public void AnElementWithOnlyText_ShowsTheTextOnItsLine_WithNoChildren()
    {
        XmlTree tree = Build("<title>Dune</title>");

        XmlNodeItem title = (XmlNodeItem)tree.Root.Children.AsSnapshot().Single();

        Assert.AreEqual("<title>Dune</title>", title.Name);
        Assert.AreEqual(0, title.Children.Count);
    }

    [TestMethod]
    public void AnEmptyElement_ShowsAsSelfClosing()
    {
        XmlTree tree = Build("<root><empty/></root>");

        XmlNodeItem empty = (XmlNodeItem)tree.Root.Children.AsSnapshot().Single().Children.AsSnapshot().Single();

        Assert.AreEqual("<empty/>", empty.Name);
    }

    [TestMethod]
    public void MixedContent_KeepsTextAndCommentsAsChildren()
    {
        XmlTree tree = Build("<p>Hello <!-- note --><b>there</b></p>");

        XmlNodeItem p = (XmlNodeItem)tree.Root.Children.AsSnapshot().Single();
        List<string> names = p.Children.AsSnapshot().Select(c => c.Name).ToList();

        CollectionAssert.AreEqual(new[] { "\"Hello\"", "<!-- note -->", "<b>there</b>" }, names);
    }

    [TestMethod]
    public void LongText_IsShortened_AndWhitespaceCollapsed()
    {
        string text = string.Concat(Enumerable.Repeat("word  ", 40));

        string shortened = XmlTree.Shorten(text);

        Assert.AreEqual(XmlTree.MaxTextLength, shortened.Length);
        Assert.IsTrue(shortened.EndsWith('…'));
        Assert.IsFalse(shortened.Contains("  "));
    }

    [TestMethod]
    public void EveryNode_IsIndexed_SoAResultCanBeShownAtItsPlace()
    {
        XDocument document = XDocument.Parse("<a><b>x</b></a>");
        XmlTree tree = XmlTree.Build(document);
        XElement b = document.Root!.Element("b")!;

        XmlNodeItem? item = tree.ItemFor(b);

        Assert.IsNotNull(item);
        Assert.AreSame(b, item.Node);
    }

    [TestMethod]
    public void ProcessingInstructions_AreShown()
    {
        XmlTree tree = Build("""<?xml-stylesheet href="style.xsl"?><root/>""");

        Assert.AreEqual(2, tree.Root.Children.Count);
        Assert.IsTrue(tree.Root.Children.AsSnapshot().First().Name.StartsWith("<?xml-stylesheet"));
    }
}
