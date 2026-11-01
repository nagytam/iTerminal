using iConsole.Xml;
using System.Xml.Linq;

namespace iConsole.Test.Xml;

[TestClass]
public class XmlXPathQueryTest
{
    private const string Catalog = """
        <catalog xmlns:store="urn:example:store">
          <book id="bk101"><title>Dune</title><price>9.50</price></book>
          <book id="bk102"><title>Emma</title><price>12.00</price></book>
          <store:note>Shelf 3</store:note>
        </catalog>
        """;

    private static XmlTree Tree(string xml = Catalog) => XmlTree.Build(XDocument.Parse(xml));

    [Timeout(60_000)]
    [TestMethod]
    public void NodeSet_GivesOneResultPerNode_WithItsAbsolutePath()
    {
        XmlXPathOutcome outcome = XmlXPathQuery.Run(Tree(), "//book/title");

        Assert.AreEqual(2, outcome.Total);
        CollectionAssert.AreEqual(new[] { "/catalog/book[1]/title", "/catalog/book[2]/title" }, outcome.Results.Select(r => r.Path).ToList());
        Assert.AreEqual("Dune", outcome.Results[0].Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Attributes_ShowTheirValue_AndTheElementToJumpTo()
    {
        XmlTree tree = Tree();

        XmlXPathOutcome outcome = XmlXPathQuery.Run(tree, "//book[price > 10]/@id");

        XmlXPathResult result = outcome.Results.Single();
        Assert.AreEqual("/catalog/book[2]/@id", result.Path);
        Assert.AreEqual("bk102", result.Value);
        Assert.AreSame(tree.ItemFor(tree.Document.Root!.Elements("book").Last()), result.Node);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AScalarExpression_GivesOneValue()
    {
        XmlXPathOutcome outcome = XmlXPathQuery.Run(Tree(), "count(//book)");

        Assert.AreEqual(1, outcome.Total);
        Assert.AreEqual("2", outcome.Results.Single().Value);
        Assert.IsNull(outcome.Results.Single().Node);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DeclaredPrefixes_CanBeUsedInTheExpression()
    {
        XmlXPathOutcome outcome = XmlXPathQuery.Run(Tree(), "//store:note");

        Assert.AreEqual(1, outcome.Total);
        Assert.AreEqual("Shelf 3", outcome.Results.Single().Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMalformedExpression_ReportsTheError_WithNoResults()
    {
        XmlXPathOutcome outcome = XmlXPathQuery.Run(Tree(), "//book[");

        Assert.IsNotNull(outcome.Error);
        Assert.AreEqual(0, outcome.Total);
        Assert.AreEqual(0, outcome.Results.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ABlankExpression_RunsNothing()
    {
        XmlXPathOutcome outcome = XmlXPathQuery.Run(Tree(), "   ");

        Assert.AreEqual(0, outcome.Total);
        Assert.IsNull(outcome.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ManyMatches_AreCountedAll_ButOnlyTheFirstShown()
    {
        string items = string.Concat(Enumerable.Range(0, XmlXPathQuery.MaxResults + 25).Select(i => $"<item>{i}</item>"));

        XmlXPathOutcome outcome = XmlXPathQuery.Run(Tree($"<list>{items}</list>"), "//item");

        Assert.AreEqual(XmlXPathQuery.MaxResults + 25, outcome.Total);
        Assert.AreEqual(XmlXPathQuery.MaxResults, outcome.Results.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathOf_NumbersOnlyRepeatedSiblings()
    {
        XDocument document = XDocument.Parse("<a><b/><c/><c/></a>");

        Assert.AreEqual("/a/b", XmlXPathQuery.PathOf(document.Root!.Element("b")!));
        Assert.AreEqual("/a/c[2]", XmlXPathQuery.PathOf(document.Root!.Elements("c").Last()));
    }
}
