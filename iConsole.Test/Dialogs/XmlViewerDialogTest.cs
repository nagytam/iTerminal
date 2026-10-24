using System.Text;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Xml;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class XmlViewerDialogTest
{
    private static readonly Url Document = new("file://C:/x/catalog.xml");

    private static XmlViewerDialog CreateDialog(byte[] content, out Mock<IFileToTextConverterService> converter)
    {
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(content);
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(Document)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        converter = new Mock<IFileToTextConverterService>();
        return new XmlViewerDialog(
            models.Object,
            converter.Object,
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            new ThemeService())
        {
            Url = Document,
        };
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [TestMethod]
    public async Task Load_BuildsTheTree_ForAWellFormedDocument()
    {
        XmlViewerDialog dialog = CreateDialog(Utf8("<catalog><book id=\"1\"/></catalog>"), out _);

        await dialog.OnInitializeAsync();

        Assert.IsNotNull(dialog.Tree);
        Assert.IsFalse(dialog.IsTextMode);
        Assert.IsTrue(dialog.Tree.Root.IsOpened);
    }

    [TestMethod]
    public async Task Load_ShowsTheTextAndTheError_ForAMalformedDocument()
    {
        XmlViewerDialog dialog = CreateDialog(Utf8("<a><b></a>"), out Mock<IFileToTextConverterService> converter);

        await dialog.OnInitializeAsync();

        Assert.IsNull(dialog.Tree);
        Assert.IsTrue(dialog.IsTextMode);
        Assert.IsTrue(dialog.Status.StartsWith("Not well-formed XML:"), dialog.Status);
        converter.Verify(c => c.ReadFileAsync(Document, It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()), Times.Once);
    }

    [TestMethod]
    public async Task Query_ShowsItsResults_AndTheMatchCount_WhenApplied()
    {
        XmlViewerDialog dialog = CreateDialog(Utf8("<catalog><book/><book/><note/></catalog>"), out _);
        await dialog.OnInitializeAsync();

        await dialog.StartQueryAsync("//book");
        dialog.OnIdle();

        Assert.AreEqual(2, dialog.Results.Count);
        Assert.AreEqual("2 match(es)", dialog.Status);
    }

    [TestMethod]
    public async Task Query_ReportsAnError_ForAnInvalidExpression()
    {
        XmlViewerDialog dialog = CreateDialog(Utf8("<catalog/>"), out _);
        await dialog.OnInitializeAsync();

        await dialog.StartQueryAsync("//[");
        dialog.OnIdle();

        Assert.IsTrue(dialog.Status.StartsWith("XPath error:"), dialog.Status);
        Assert.AreEqual(0, dialog.Results.Count);
    }

    [TestMethod]
    public async Task Reveal_OpensTheAncestors_AndFocusesTheNode()
    {
        XmlViewerDialog dialog = CreateDialog(Utf8("<a><b><c><d>deep</d></c></b></a>"), out _);
        await dialog.OnInitializeAsync();
        await dialog.StartQueryAsync("//d");
        dialog.OnIdle();
        XmlNodeItem node = dialog.Results.Single().Node!;
        Assert.IsFalse(node.Parent!.IsOpened, "The ancestors start collapsed below the first two levels.");

        dialog.Reveal(node);

        for (IHierarchicalItemModel? ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            Assert.IsTrue(ancestor.IsOpened);
        }

        Assert.IsTrue(node.IsFocused);
        Assert.IsFalse(dialog.Tree!.Root.IsFocused);
    }

    [TestMethod]
    public async Task ToggleTextMode_SwitchesToTheFilesText_AndBack()
    {
        XmlViewerDialog dialog = CreateDialog(Utf8("<catalog/>"), out Mock<IFileToTextConverterService> converter);
        await dialog.OnInitializeAsync();

        await dialog.ToggleTextModeAsync();
        Assert.IsTrue(dialog.IsTextMode);
        await dialog.ToggleTextModeAsync();
        Assert.IsFalse(dialog.IsTextMode);

        converter.Verify(c => c.ReadFileAsync(Document, It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()), Times.Once);
    }

    [TestMethod]
    public async Task SlashWithTheTreeFocused_MovesTheFocusToTheXPathBox()
    {
        XmlViewerDialog dialog = CreateDialog(Utf8("<catalog/>"), out _);
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();
        dialog.OnInitializeLayout();
        Assert.IsTrue(dialog.Tree!.Root.IsFocused);

        bool handled = await dialog.HandleKeyEventAsync(new KeyEvent('/', ConsoleKey.Oem2, false, false, false));

        Assert.IsTrue(handled);
        Assert.IsFalse(dialog.IsTextMode);
        Assert.IsTrue(XPathBoxOf(dialog).IsFocused);
    }

    [TestMethod]
    public async Task Star_ExpandsEveryNode()
    {
        XmlViewerDialog dialog = CreateDialog(Utf8("<a><b><c><d/></c></b></a>"), out _);
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();
        dialog.OnInitializeLayout();

        bool handled = await dialog.HandleKeyEventAsync(new KeyEvent('*', ConsoleKey.Multiply, false, false, false));

        Assert.IsTrue(handled);
        XmlNodeItem c = (XmlNodeItem)dialog.Tree!.Root.Children.AsSnapshot().Single().Children.AsSnapshot().Single().Children.AsSnapshot().Single();
        Assert.IsTrue(c.IsOpened);
    }

    [TestMethod]
    public async Task Enter_InTheXPathBox_RunsTheTypedQuery()
    {
        XmlViewerDialog dialog = CreateDialog(Utf8("<catalog><book/><book/></catalog>"), out _);
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();
        dialog.OnInitializeLayout();
        XPathBoxOf(dialog).Text = "//book";
        XPathBoxOf(dialog).SetFocus();

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));
        await Task.Delay(50);
        dialog.OnIdle();

        Assert.AreEqual(2, dialog.Results.Count);
    }

    private static TextBox XPathBoxOf(XmlViewerDialog dialog) =>
        (TextBox)dialog.Controls.First(c => c is TextBox);

    [TestMethod]
    public async Task F2_ListsTheValidationErrors_AndEnterOpensTheElement()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xmlviewer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "book.xsd"), """
                <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
                  <xs:element name="book"><xs:complexType><xs:sequence><xs:element name="title" type="xs:string"/></xs:sequence></xs:complexType></xs:element>
                </xs:schema>
                """);
            string xml = """
                <book xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="book.xsd">
                  <author>Frank Herbert</author>
                </book>
                """;
            string path = Path.Combine(directory, "wrong.xml");
            File.WriteAllText(path, xml);
            Url url = new(path);
            Mock<IMemoryFile> file = new();
            file.SetupGet(f => f.Content).Returns(Utf8(xml));
            Mock<IContentModel> fileSystem = new();
            fileSystem.Setup(f => f.ReadFileAsync(url)).ReturnsAsync(file.Object);
            Mock<IContentModelService> models = new();
            models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
            XmlViewerDialog dialog = new(models.Object, Mock.Of<IFileToTextConverterService>(), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), new ThemeService()) { Url = url };
            await dialog.OnInitializeAsync();
            await dialog.ShowAsync();
            dialog.OnInitializeLayout();

            bool handled = await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F2, false, false, false));
            await dialog.StartValidationAsync().ContinueWith(_ => { });
            dialog.OnIdle();

            Assert.IsTrue(handled);
            Assert.IsTrue(dialog.Status.Contains("error"), dialog.Status);
            XmlXPathResult issue = dialog.Results.First(r => r.Value.Contains("author"));
            Assert.IsTrue(issue.Path.StartsWith("error at line 2"), issue.Path);
            Assert.AreEqual("<author>", issue.Node!.Name.Substring(0, 8));

            dialog.Reveal(issue.Node!);
            Assert.IsTrue(issue.Node!.IsFocused);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task F2_OnADocumentWithNoSchema_SaysSoOnTheStatusLine()
    {
        XmlViewerDialog dialog = CreateDialog(Utf8("<plain/>"), out _);
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();

        await dialog.StartValidationAsync();
        dialog.OnIdle();

        Assert.IsTrue(dialog.Status.Contains("nothing to validate"), dialog.Status);
    }
}
