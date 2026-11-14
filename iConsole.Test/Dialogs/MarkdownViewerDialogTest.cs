using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Common;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class MarkdownViewerDialogTest
{
    private static MarkdownViewerDialog CreateDialog(string path, byte[] content)
    {
        var document = new Url(path);
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(content);
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(document)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        return new MarkdownViewerDialog(models.Object, Mock.Of<IFileToTextConverterService>(), new ThemeService()) { Url = document };
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpensTheExampleDocumentRendered()
    {
        MarkdownViewerDialog dialog = CreateDialog("file:///docs/example.md", SampleFiles.Bytes("example.md"));

        await dialog.OnInitializeAsync();

        StringAssert.StartsWith(dialog.Markdown, "# iTerminal example");
        Assert.AreEqual(string.Empty, dialog.Status);
        Assert.IsFalse(dialog.IsTextMode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F4SwitchesToTheTextAndBackAgain()
    {
        MarkdownViewerDialog dialog = CreateDialog("file:///docs/example.md", SampleFiles.Bytes("example.md"));
        await dialog.OnInitializeAsync();

        await dialog.ToggleTextModeAsync();
        Assert.IsTrue(dialog.IsTextMode);

        await dialog.ToggleTextModeAsync();
        Assert.IsFalse(dialog.IsTextMode);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALatin1FileIsDecodedAsItIsRead()
    {
        byte[] content = System.Text.Encoding.Latin1.GetBytes("# Café\n");
        MarkdownViewerDialog dialog = CreateDialog("file:///docs/cafe.md", content);

        await dialog.OnInitializeAsync();

        StringAssert.Contains(dialog.Markdown, "# Caf");
        Assert.AreEqual(TextEncodings.Decode(content, out _), dialog.Markdown);
    }
}
