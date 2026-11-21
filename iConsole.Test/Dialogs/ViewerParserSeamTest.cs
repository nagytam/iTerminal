using System.Text;
using System.Xml.Linq;
using iConsole.Certificates;
using iConsole.Csv;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Har;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Json;
using iConsole.Markdown;
using iConsole.Model;
using iConsole.Services;
using iConsole.Xml;
using iConsole.Xps;
using Moq;

namespace iConsole.Test.Dialogs;

/// <summary>
/// Each viewer takes its parser through an interface. These tests inject a fake whose result differs from the file's own
/// content, and check that the dialog shows the fake's answer. A dialog that still calls the parser directly cannot pass.
/// </summary>
[TestClass]
public class ViewerParserSeamTest
{
    private static readonly Url Document = new("file:///data/seam.txt");

    private static Mock<IContentModelService> Models(byte[] content)
    {
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(content);
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(Document)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        return models;
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task CsvShowsTheDocumentTheInjectedParserReturns()
    {
        Mock<ICsvDocumentParser> parser = new();
        parser.Setup(p => p.Parse(It.IsAny<string>(), It.IsAny<char?>(), It.IsAny<bool>()))
            .Returns(new CsvDocument(["Fake"], [["a"], ["b"], ["c"]], ',', false));
        var dialog = new CsvViewerDialog(Models("real,file\n1,2\n"u8.ToArray()).Object, Mock.Of<IFileToTextConverterService>(), parser.Object,
            Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), new ThemeService(), Mock.Of<IPromptService>()) { Url = Document };

        await dialog.OnInitializeAsync();

        Assert.IsNotNull(dialog.Document);
        CollectionAssert.AreEqual(new[] { "Fake" }, dialog.Document.Headers.ToArray());
        Assert.AreEqual(3, dialog.Document.Rows.Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task HarShowsTheRowsTheInjectedParserReturns()
    {
        Mock<IHarDocumentParser> parser = new();
        parser.Setup(p => p.Parse(It.IsAny<string>()))
            .Returns(new HarDocument([new HarEntry(1, "FAKE", "https://fake.test/", 418, "Teapot", "text/plain", null, 1, string.Empty, default)], false));
        var dialog = new HarViewerDialog(Models("{\"log\":{\"entries\":[]}}"u8.ToArray()).Object, Mock.Of<IFileToTextConverterService>(), parser.Object, new ThemeService()) { Url = Document };

        await dialog.OnInitializeAsync();

        Assert.AreEqual(1, dialog.Grid.RowCount);
        Assert.AreEqual("1 request(s)", dialog.Status);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task XmlShowsTheTreeTheInjectedParserReturns()
    {
        Mock<IXmlDocumentParser> parser = new();
        parser.Setup(p => p.Parse(It.IsAny<byte[]>())).Returns(new XDocument(new XElement("fake-root")));
        Mock<IXmlValidator> validator = new();
        validator.Setup(v => v.Validate(It.IsAny<byte[]>(), It.IsAny<string?>())).Returns([]);
        var dialog = new XmlViewerDialog(Models("<real/>"u8.ToArray()).Object, Mock.Of<IFileToTextConverterService>(), parser.Object, validator.Object,
            Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), new ThemeService()) { Url = Document };

        await dialog.OnInitializeAsync();

        Assert.IsNotNull(dialog.Tree);
        Assert.AreEqual("fake-root", dialog.Tree.Document.Root!.Name.LocalName);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task JsonShowsTheVerdictTheInjectedValidatorReturns()
    {
        Mock<IJsonValidator> validator = new();
        validator.Setup(v => v.Validate(It.IsAny<byte[]>())).Returns(new JsonValidationIssue(3, 4, "FAKE VERDICT"));
        var dialog = new JsonViewerDialog(Models("{}"u8.ToArray()).Object, Mock.Of<IFileToTextConverterService>(), validator.Object, new ThemeService()) { Url = Document };

        await dialog.OnInitializeAsync();

        StringAssert.Contains(dialog.Status, "FAKE VERDICT");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task XpsShowsThePagesTheInjectedReaderReturns()
    {
        Mock<IXpsDocumentReader> reader = new();
        reader.Setup(r => r.Read(It.IsAny<byte[]>()))
            .Returns(new XpsDocument([new XpsPage(1, 1, 1, 100, 100, ["FAKE LINE"])], title: null, creator: null, error: null));
        var dialog = new XpsViewerDialog(Models("not read"u8.ToArray()).Object, reader.Object, new ThemeService()) { Url = Document };

        await dialog.OnInitializeAsync();

        Assert.IsNotNull(dialog.Document);
        Assert.AreEqual("FAKE LINE", dialog.Document.Pages[0].Lines[0]);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task MarkdownParsesTheSourceThroughTheInjectedParser()
    {
        Mock<IMarkdownParser> parser = new();
        parser.Setup(p => p.Parse(It.IsAny<string>())).Returns([]);
        var dialog = new MarkdownViewerDialog(Models("# Real"u8.ToArray()).Object, Mock.Of<IFileToTextConverterService>(), parser.Object, new ThemeService()) { Url = Document };

        await dialog.OnInitializeAsync();

        Assert.AreEqual("# Real", dialog.Markdown);
        parser.Verify(p => p.Parse("# Real"), Times.Once);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task CertificateReadsTheFileAndParsesItThroughTheInjectedReader()
    {
        string path = Path.Combine(Path.GetTempPath(), $"SeamTest_{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, "not a certificate");
        try
        {
            Mock<ICertificateReader> reader = new();
            reader.Setup(r => r.ReadFile(It.IsAny<string>())).Returns(new CertificateFileRead(CertificateFileStatus.Ok, "not a certificate"u8.ToArray()));
            reader.Setup(r => r.Read(It.IsAny<ReadOnlyMemory<byte>>())).Returns(new CertificateReadResult([], 0, "FAKE READER"));
            var dialog = new CertificateViewerDialog(new ThemeService(), Mock.Of<INotificationService>(), reader.Object) { Url = new Url(path) };

            await dialog.OnInitializeAsync();
            await dialog.LoadTask;

            reader.Verify(r => r.ReadFile(It.IsAny<string>()), Times.Once);
            reader.Verify(r => r.Read(It.IsAny<ReadOnlyMemory<byte>>()), Times.Once);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
