using iTerminal.Core.Model.Web;

namespace iTerminal.Core.Test.Model.Web;

[TestClass]
public class WebAddressTest
{
    [TestMethod]
    [DataRow("example.com", "https://example.com")]
    [DataRow("www.example.com/path?x=1", "https://www.example.com/path?x=1")]
    [DataRow("http://example.com/a", "http://example.com/a")]
    [DataRow("localhost:8080/x", "http://localhost:8080/x")]
    [DataRow("127.0.0.1:5000", "http://127.0.0.1:5000")]
    [DataRow("about:blank", "about:blank")]
    [DataRow("", "about:blank")]
    public void TypedAddressesBecomeUrls(string input, string expected)
    {
        Assert.AreEqual(expected, WebAddress.FromInput(input));
    }

    [TestMethod]
    public void WordsAreSearched()
    {
        Assert.AreEqual("https://html.duckduckgo.com/html/?q=cats+and+dogs", WebAddress.FromInput("cats and dogs"));
        Assert.AreEqual("https://html.duckduckgo.com/html/?q=word", WebAddress.FromInput("word"));
        Assert.AreEqual("https://search.test/?q=a+b", WebAddress.FromInput("a b", "https://search.test/?q={0}"));
    }

    [TestMethod]
    public void PathsBecomeFileUrls()
    {
        var path = Path.Combine(Path.GetTempPath(), "page.html");

        var url = WebAddress.FromInput(path);

        Assert.IsTrue(url.StartsWith("file:///", StringComparison.Ordinal), url);
    }

    [TestMethod]
    public void RelativeLinksResolveAgainstThePage()
    {
        Assert.AreEqual("https://a.test/dir/x.html", WebAddress.Resolve("https://a.test/dir/page.html", "x.html"));
        Assert.AreEqual("https://a.test/x", WebAddress.Resolve("https://a.test/dir/page.html", "/x"));
        Assert.AreEqual("https://a.test/dir/page.html#top", WebAddress.Resolve("https://a.test/dir/page.html", "#top"));
        Assert.AreEqual("https://b.test/", WebAddress.Resolve("https://a.test/dir/page.html", "https://b.test/"));
        Assert.AreEqual("javascript:void(0)", WebAddress.Resolve("https://a.test/", "javascript:void(0)"));
        Assert.AreEqual("https://c.test/p", WebAddress.Resolve("https://a.test/", "//c.test/p"));
    }

    [TestMethod]
    public void FragmentsAreSplitOff()
    {
        Assert.AreEqual("https://a.test/p", WebAddress.WithoutFragment("https://a.test/p#x"));
        Assert.AreEqual("x y", WebAddress.Fragment("https://a.test/p#x%20y"));
        Assert.IsTrue(WebAddress.IsSameDocument("https://a.test/p#1", "https://a.test/p#2"));
        Assert.IsFalse(WebAddress.IsSameDocument("https://a.test/p", "https://a.test/q"));
    }
}
