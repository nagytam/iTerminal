using iTerminal.Core.Model.Web;

namespace iTerminal.Core.Test.Model.Web;

[TestClass]
public class HtmlFileTypesTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("/site/index.html")]
    [DataRow("/site/index.htm")]
    [DataRow("/site/page.xhtml")]
    [DataRow("/site/PAGE.HTML")]
    [DataRow("/site/Page.Xhtml")]
    public void IsHtmlFile_ForEveryHtmlExtension_IsTrue(string path) =>
        Assert.IsTrue(HtmlFileTypes.IsHtmlFile(path));

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("/site/data.xml")]
    [DataRow("/site/readme.txt")]
    [DataRow("/site/page.html.bak")]
    [DataRow("/site/html")]
    [DataRow("")]
    [DataRow(null)]
    public void IsHtmlFile_ForAnythingElse_IsFalse(string? path) =>
        Assert.IsFalse(HtmlFileTypes.IsHtmlFile(path));
}
