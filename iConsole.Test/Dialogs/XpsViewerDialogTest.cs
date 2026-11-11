using System.IO.Compression;
using System.Text;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

/// <summary>
/// The XPS viewer: the package's pages in a picker, the selected page's text below it, and the page detail on the
/// status line. The viewer reads through the same model service as the other viewers, so a file that cannot be read
/// says so instead of opening as an empty list.
/// </summary>
[TestClass]
public class XpsViewerDialogTest
{
    private const string Namespace = "http://schemas.microsoft.com/xps/2005/06";

    private static readonly Url Document = new("file://C:/x/report.xps");

    private static byte[] MakePackage(params (string Name, string Xml)[] parts)
    {
        using MemoryStream buffer = new();
        using (ZipArchive zip = new(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string xml) in parts)
            {
                using StreamWriter writer = new(zip.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(xml);
            }
        }

        return buffer.ToArray();
    }

    private static byte[] Package(params string[] pageTexts)
    {
        List<(string Name, string Xml)> parts =
        [
            ("_rels/.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="R1" Type="http://schemas.microsoft.com/xps/2005/06/fixedrepresentation" Target="/FixedDocSeq.fdseq"/>
                </Relationships>
                """),
            ("FixedDocSeq.fdseq", $"""
                <FixedDocumentSequence xmlns="{Namespace}">
                  <DocumentReference Source="Documents/1/FixedDocument.fdoc"/>
                </FixedDocumentSequence>
                """),
            ("Documents/1/FixedDocument.fdoc", $"""
                <FixedDocument xmlns="{Namespace}">
                  {string.Join("\n", pageTexts.Select((_, index) => $"""<PageContent Source="Pages/{index + 1}.fpage"/>"""))}
                </FixedDocument>
                """),
            .. pageTexts.Select((text, index) => ($"Documents/1/Pages/{index + 1}.fpage", $"""
                <FixedPage xmlns="{Namespace}" Width="816" Height="1056">
                  <Glyphs OriginX="10" OriginY="50" UnicodeString="{text}"/>
                </FixedPage>
                """)),
            ("docProps/core.xml", """
                <cp:coreProperties
                    xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
                    xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <dc:title>Quarterly report</dc:title>
                </cp:coreProperties>
                """),
        ];

        return MakePackage([.. parts]);
    }

    private static Mock<IContentModel> FileSystemWith(byte[]? content)
    {
        Mock<IContentModel> fileSystem = new();
        if (content is null)
        {
            fileSystem.Setup(f => f.ReadFileAsync(Document)).ReturnsAsync((IMemoryFile?)null);
        }
        else
        {
            Mock<IMemoryFile> file = new();
            file.SetupGet(f => f.Content).Returns(content);
            fileSystem.Setup(f => f.ReadFileAsync(Document)).ReturnsAsync(file.Object);
        }

        return fileSystem;
    }

    private static XpsViewerDialog CreateDialog(byte[]? content, out Mock<IContentModelService> models)
    {
        models = new Mock<IContentModelService>();
        models.Setup(m => m.GetFocusedModel()).Returns(FileSystemWith(content).Object);
        return new XpsViewerDialog(models.Object, new ThemeService()) { Url = Document };
    }

    private static XpsViewerDialog CreateDialog(byte[]? content) => CreateDialog(content, out _);

    [Timeout(60_000)]
    [TestMethod]
    public async Task Load_ShowsTheFirstPagesTextAndItsPlace()
    {
        XpsViewerDialog dialog = CreateDialog(Package("First page", "Second page"));

        await dialog.OnInitializeAsync();

        Assert.IsTrue(dialog.Document?.IsValid, dialog.Document?.Error);
        Assert.AreEqual(2, dialog.Document!.Pages.Count);
        Assert.AreEqual(1, dialog.CurrentPage?.Number);
        CollectionAssert.Contains(dialog.Lines.ToList(), "First page");
        StringAssert.StartsWith(dialog.Status, "Page 1 of 2");
        StringAssert.Contains(dialog.Status, "816 × 1056");
        StringAssert.Contains(dialog.Status, "Quarterly report");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StepPage_MovesThroughThePagesAndStopsAtTheEnds()
    {
        XpsViewerDialog dialog = CreateDialog(Package("First page", "Second page"));
        await dialog.OnInitializeAsync();

        dialog.StepPage(1);
        Assert.AreEqual(2, dialog.CurrentPage?.Number);
        CollectionAssert.Contains(dialog.Lines.ToList(), "Second page");

        dialog.StepPage(1);
        Assert.AreEqual(2, dialog.CurrentPage?.Number, "the last page is the end of the package");

        dialog.StepPage(-5);
        Assert.AreEqual(1, dialog.CurrentPage?.Number, "the first page is the start of the package");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StepPage_WithControlArrows_MovesFromTheText()
    {
        XpsViewerDialog dialog = CreateDialog(Package("First page", "Second page"));
        await dialog.OnInitializeAsync();

        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.RightArrow, shift: false, alt: false, control: true)));
        Assert.AreEqual(2, dialog.CurrentPage?.Number);

        Assert.IsTrue(await dialog.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.LeftArrow, shift: false, alt: false, control: true)));
        Assert.AreEqual(1, dialog.CurrentPage?.Number);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Load_APageWithoutText_SaysSo()
    {
        XpsViewerDialog dialog = CreateDialog(MakePackage(
            ("_rels/.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="R1" Type="http://schemas.microsoft.com/xps/2005/06/fixedrepresentation" Target="/FixedDocSeq.fdseq"/>
                </Relationships>
                """),
            ("FixedDocSeq.fdseq", $"""
                <FixedDocumentSequence xmlns="{Namespace}">
                  <DocumentReference Source="Documents/1/FixedDocument.fdoc"/>
                </FixedDocumentSequence>
                """),
            ("Documents/1/FixedDocument.fdoc", $"""
                <FixedDocument xmlns="{Namespace}">
                  <PageContent Source="Pages/1.fpage"/>
                </FixedDocument>
                """),
            ("Documents/1/Pages/1.fpage", $"""
                <FixedPage xmlns="{Namespace}">
                  <Path Data="M 10,10 L 100,100"/>
                </FixedPage>
                """)));

        await dialog.OnInitializeAsync();

        Assert.AreEqual(1, dialog.CurrentPage?.Number);
        CollectionAssert.Contains(dialog.Lines.ToList(), "This page has no text.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Load_APackageWithoutPages_SaysSo()
    {
        XpsViewerDialog dialog = CreateDialog(MakePackage(
            ("_rels/.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="R1" Type="http://schemas.microsoft.com/xps/2005/06/fixedrepresentation" Target="/FixedDocSeq.fdseq"/>
                </Relationships>
                """),
            ("FixedDocSeq.fdseq", $"""<FixedDocumentSequence xmlns="{Namespace}"/>""")));

        await dialog.OnInitializeAsync();

        Assert.AreEqual("This document has no pages", dialog.Status);
        Assert.IsNull(dialog.CurrentPage);
        Assert.AreEqual(0, dialog.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Load_ABrokenPackage_ShowsWhyInsteadOfAnEmptyList()
    {
        XpsViewerDialog dialog = CreateDialog(MakePackage(("Other.xml", "<x/>")));

        await dialog.OnInitializeAsync();

        Assert.IsFalse(dialog.Document?.IsValid);
        StringAssert.StartsWith(dialog.Status, "[Could not read the document:");
        Assert.AreEqual(0, dialog.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Load_WhenTheFileCannotBeRead_ShowsThat()
    {
        XpsViewerDialog dialog = CreateDialog(content: null);

        await dialog.OnInitializeAsync();

        Assert.AreEqual("Could not read the file", dialog.Status);
        Assert.IsNull(dialog.Document);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PagePicker_LabelsPagesOfSeveralDocuments()
    {
        XpsViewerDialog dialog = CreateDialog(MakePackage(
            ("_rels/.rels", $"""
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="R1" Type="http://schemas.microsoft.com/xps/2005/06/fixedrepresentation" Target="/FixedDocSeq.fdseq"/>
                </Relationships>
                """),
            ("FixedDocSeq.fdseq", $"""
                <FixedDocumentSequence xmlns="{Namespace}">
                  <DocumentReference Source="Documents/1/FixedDocument.fdoc"/>
                  <DocumentReference Source="Documents/2/FixedDocument.fdoc"/>
                </FixedDocumentSequence>
                """),
            ("Documents/1/FixedDocument.fdoc", $"""
                <FixedDocument xmlns="{Namespace}">
                  <PageContent Source="Pages/1.fpage"/>
                </FixedDocument>
                """),
            ("Documents/2/FixedDocument.fdoc", $"""
                <FixedDocument xmlns="{Namespace}">
                  <PageContent Source="Pages/1.fpage"/>
                </FixedDocument>
                """),
            ("Documents/1/Pages/1.fpage", $"""<FixedPage xmlns="{Namespace}"><Glyphs UnicodeString="one"/></FixedPage>"""),
            ("Documents/2/Pages/1.fpage", $"""<FixedPage xmlns="{Namespace}"><Glyphs UnicodeString="two"/></FixedPage>""")));

        await dialog.OnInitializeAsync();

        Assert.AreEqual("Document 1 · Page 1", dialog.PagePicker.DisplaySelector(dialog.PagePicker.Items[0]));
        Assert.AreEqual("Document 2 · Page 1", dialog.PagePicker.DisplaySelector(dialog.PagePicker.Items[1]));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Load_Twice_ReplacesTheFirstPackagesPages()
    {
        XpsViewerDialog dialog = CreateDialog(Package("First page"), out Mock<IContentModelService> models);
        await dialog.OnInitializeAsync();
        Assert.AreEqual(1, dialog.Document?.Pages.Count);

        // The same dialog instance serves every package a panel views.
        models.Setup(m => m.GetFocusedModel()).Returns(FileSystemWith(Package("First page", "Second page")).Object);
        await dialog.OnInitializeAsync();

        Assert.AreEqual(2, dialog.Document?.Pages.Count);
        Assert.AreEqual(1, dialog.CurrentPage?.Number);
        CollectionAssert.Contains(dialog.Lines.ToList(), "First page");
    }
}
