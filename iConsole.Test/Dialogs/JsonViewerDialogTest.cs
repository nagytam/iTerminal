using System.Text;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Json;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class JsonViewerDialogTest
{
    private static readonly Url Document = new("file://C:/x/settings.json");

    private static JsonViewerDialog CreateDialog(string json, out Mock<IFileToTextConverterService> converter)
    {
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(Encoding.UTF8.GetBytes(json));
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(Document)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        converter = new Mock<IFileToTextConverterService>();
        return new JsonViewerDialog(models.Object, converter.Object, new ThemeService()) { Url = Document };
    }

    [TestMethod]
    public async Task Load_BuildsTheTree_ForValidJson()
    {
        JsonViewerDialog dialog = CreateDialog("""{"a":1,"b":{"c":2}}""", out _);

        await dialog.OnInitializeAsync();

        Assert.IsNotNull(dialog.Root);
        Assert.IsFalse(dialog.IsTextMode);
        Assert.IsTrue(dialog.Root.IsOpened);
        Assert.AreEqual("2 top-level item(s)", dialog.Status);
    }

    [TestMethod]
    public async Task Load_ShowsTheTextAndTheError_ForInvalidJson()
    {
        JsonViewerDialog dialog = CreateDialog("{\"a\": 1,}", out Mock<IFileToTextConverterService> converter);

        await dialog.OnInitializeAsync();

        Assert.IsNull(dialog.Root);
        Assert.IsTrue(dialog.IsTextMode);
        Assert.IsTrue(dialog.Status.StartsWith("Not valid JSON: line 1"), dialog.Status);
        converter.Verify(c => c.ReadFileAsync(Document, It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()), Times.Once);
    }

    [TestMethod]
    public async Task F2_OnValidJson_SaysSo()
    {
        JsonViewerDialog dialog = CreateDialog("[1,2]", out _);
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();

        await dialog.StartValidationAsync();
        dialog.OnIdle();

        Assert.AreEqual("Valid JSON", dialog.Status);
    }

    [TestMethod]
    public async Task F2_OnInvalidJson_ReportsWhereItStops()
    {
        JsonViewerDialog dialog = CreateDialog("[1, 2,]", out _);
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();

        bool handled = await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F2, false, false, false));
        await dialog.StartValidationAsync();
        dialog.OnIdle();

        Assert.IsTrue(handled);
        Assert.IsTrue(dialog.Status.StartsWith("Not valid JSON: line 1"), dialog.Status);
    }

    [TestMethod]
    public async Task Star_ExpandsEveryNode()
    {
        JsonViewerDialog dialog = CreateDialog("""{"a":{"b":{"c":1}}}""", out _);
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();
        dialog.OnInitializeLayout();

        bool handled = await dialog.HandleKeyEventAsync(new KeyEvent('*', ConsoleKey.Multiply, false, false, false));

        Assert.IsTrue(handled);
        JsonNodeItem b = (JsonNodeItem)dialog.Root!.Children.AsSnapshot().Single().Children.AsSnapshot().Single();
        Assert.IsTrue(b.IsOpened);
    }

    [TestMethod]
    public async Task Reveal_OpensTheAncestors_AndFocusesTheNode()
    {
        JsonViewerDialog dialog = CreateDialog("""{"a":{"b":{"c":{"d":1}}}}""", out _);
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();
        JsonNodeItem deep = (JsonNodeItem)dialog.Root!.Children.AsSnapshot().Single()
            .Children.AsSnapshot().Single().Children.AsSnapshot().Single();

        dialog.Reveal(deep);

        for (var ancestor = deep.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            Assert.IsTrue(ancestor.IsOpened);
        }

        Assert.IsTrue(deep.IsFocused);
    }

    [TestMethod]
    public async Task ToggleTextMode_SwitchesToTheFilesText_AndBack()
    {
        JsonViewerDialog dialog = CreateDialog("{}", out Mock<IFileToTextConverterService> converter);
        await dialog.OnInitializeAsync();

        await dialog.ToggleTextModeAsync();
        Assert.IsTrue(dialog.IsTextMode);
        await dialog.ToggleTextModeAsync();
        Assert.IsFalse(dialog.IsTextMode);

        converter.Verify(c => c.ReadFileAsync(Document, It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()), Times.Once);
    }
}
