using System.Text.Json;
using iConsole.Json;

namespace iConsole.Test.Json;

[TestClass]
public class JsonTreeTest
{
    private static JsonNodeItem Build(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonTree.Build(document);
    }

    [TestMethod]
    public void AnObject_ShowsItsMemberCount_AndEachMemberAsAKeyAndValue()
    {
        JsonNodeItem root = Build("""{"name":"Ann","age":31,"active":true}""");

        Assert.AreEqual("{ 3 }", root.Name);
        CollectionAssert.AreEqual(
            new[] { "\"name\": \"Ann\"", "\"age\": 31", "\"active\": true" },
            root.Children.AsSnapshot().Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void AnArray_ShowsIndexes_AndItsCount()
    {
        JsonNodeItem root = Build("[10, null, \"x\"]");

        Assert.AreEqual("[ 3 ]", root.Name);
        CollectionAssert.AreEqual(
            new[] { "[0]: 10", "[1]: null", "[2]: \"x\"" },
            root.Children.AsSnapshot().Select(c => c.Name).ToList());
    }

    [TestMethod]
    public void NestedContainers_HaveChildren_ScalarsAndEmptyOnesDoNot()
    {
        JsonNodeItem root = Build("""{"tags":["a","b"],"meta":{},"n":1}""");

        JsonNodeItem tags = root.Children.AsSnapshot().OfType<JsonNodeItem>().First(c => c.Name.StartsWith("\"tags\""));
        JsonNodeItem meta = root.Children.AsSnapshot().OfType<JsonNodeItem>().First(c => c.Name.StartsWith("\"meta\""));
        JsonNodeItem n = root.Children.AsSnapshot().OfType<JsonNodeItem>().First(c => c.Name.StartsWith("\"n\""));

        Assert.AreEqual("\"tags\": [ 2 ]", tags.Name);
        Assert.AreEqual(2, tags.Children.Count);
        Assert.AreEqual("\"meta\": { 0 }", meta.Name);
        Assert.AreEqual(0, meta.Children.Count);
        Assert.AreEqual(0, n.Children.Count);
    }

    [TestMethod]
    public void LongStrings_AreCut_AndLineBreaksCollapse()
    {
        string text = "word\n" + new string('x', 300);

        string shortened = JsonTree.Shorten(text);

        Assert.AreEqual(JsonTree.MaxTextLength, shortened.Length);
        Assert.IsTrue(shortened.EndsWith('…'));
        Assert.IsFalse(shortened.Contains('\n'));
    }

    [TestMethod]
    public void AScalarRoot_IsALeaf()
    {
        JsonNodeItem root = Build("\"just text\"");

        Assert.AreEqual("\"just text\"", root.Name);
        Assert.AreEqual(0, root.Children.Count);
    }

    [TestMethod]
    public void AKeyWithAQuote_IsEscaped()
    {
        JsonNodeItem root = Build("""{"say \"hi\"":1}""");

        Assert.AreEqual("\"say \\\"hi\\\"\": 1", root.Children.AsSnapshot().Single().Name);
    }
}
