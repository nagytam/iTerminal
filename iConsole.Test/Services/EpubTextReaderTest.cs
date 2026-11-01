using System.IO.Compression;
using System.Text;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading an EPUB: the package's spine gives the chapter order, and each chapter's markup becomes lines.</summary>
[TestClass]
public class EpubTextReaderTest
{
    private const string Container = """
        <?xml version="1.0"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
        </container>
        """;

    private const string Package = """
        <?xml version="1.0"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
          <manifest>
            <item id="second" href="text/second%20part.xhtml" media-type="application/xhtml+xml"/>
            <item id="first" href="text/first.xhtml" media-type="application/xhtml+xml"/>
            <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml"/>
          </manifest>
          <spine><itemref idref="first"/><itemref idref="second"/></spine>
        </package>
        """;

    private static string Chapter(string body) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <html xmlns="http://www.w3.org/1999/xhtml"><head><title>t</title><style>.c</style></head>
        <body>{body}</body></html>
        """;

    private static byte[] MakeEpub(params (string Name, string Text)[] entries)
    {
        using MemoryStream buffer = new();
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in entries)
            {
                using StreamWriter writer = new(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Chapters_FollowTheSpine_NotTheZipOrder()
    {
        byte[] epub = MakeEpub(
            ("META-INF/container.xml", Container),
            ("OEBPS/content.opf", Package),
            ("OEBPS/text/first.xhtml", Chapter("<h1>One</h1><p>Alpha</p>")),
            ("OEBPS/text/second part.xhtml", Chapter("<h2>Two</h2><p>Beta</p>")));

        List<string> lines = [.. EpubTextReader.Read(epub)];

        CollectionAssert.AreEqual(new[] { "# One", "Alpha", string.Empty, "## Two", "Beta" }, lines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HrefsAreUnescaped_AndTheNavigationDocumentIsNotInTheSpine()
    {
        byte[] epub = MakeEpub(
            ("META-INF/container.xml", Container),
            ("OEBPS/content.opf", Package),
            ("OEBPS/text/first.xhtml", Chapter("<p>First</p>")),
            ("OEBPS/text/second part.xhtml", Chapter("<p>Second</p>")),
            ("OEBPS/nav.xhtml", Chapter("<p>Contents only</p>")));

        List<string> lines = [.. EpubTextReader.Read(epub)];

        Assert.IsTrue(lines.Contains("Second"), string.Join("\n", lines));
        Assert.IsFalse(lines.Any(l => l.Contains("Contents only")), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScriptsAndStyles_AreDropped_AndLineBreaksAreKept()
    {
        byte[] epub = MakeEpub(
            ("META-INF/container.xml", Container),
            ("OEBPS/content.opf", Package),
            ("OEBPS/text/first.xhtml", Chapter("<script>var hidden = 1;</script><p>Line one<br/>Line two</p><ul><li>item</li></ul>")),
            ("OEBPS/text/second part.xhtml", Chapter("<p>x</p>")));

        List<string> lines = [.. EpubTextReader.Read(epub)];

        Assert.IsFalse(lines.Any(l => l.Contains("hidden") || l.Contains("color")), string.Join("\n", lines));
        CollectionAssert.Contains(lines, "Line one");
        CollectionAssert.Contains(lines, "Line two");
        CollectionAssert.Contains(lines, "• item");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutAContainer_TheReaderSaysSo()
    {
        byte[] epub = MakeEpub(("OEBPS/content.opf", Package));

        List<string> lines = [.. EpubTextReader.Read(epub)];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the book:"), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NotAZipFile_TheReaderSaysSo()
    {
        List<string> lines = [.. EpubTextReader.Read(Encoding.UTF8.GetBytes("plainly not an epub"))];

        Assert.IsTrue(lines.Single().StartsWith("[Could not read the book:"), string.Join("\n", lines));
    }
}
