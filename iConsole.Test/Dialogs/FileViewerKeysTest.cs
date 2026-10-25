using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

/// <summary>Keys sent to the viewer dialog, the way the app sends them — to the dialog, not to the text control.</summary>
[TestClass]
public class FileViewerKeysTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static async Task<FileViewerDialog> OpenAsync(int lines)
    {
        Url url = new("file://C:/x/long.txt");
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", Enumerable.Range(0, lines).Select(i => "row " + i))));
        Mock<IContentModel> model = new();
        model.Setup(m => m.ExistsFile(url)).Returns(true);
        model.Setup(m => m.ReadFileAsync(url)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(model.Object);
        FileViewerDialog dialog = new(models.Object, new FileToTextConverterService(), Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(), Theme()) { Url = url };
        Control root = new Window(Theme()) { ViewSize = new Size(100, 30) };
        await root.ShowDialogAsync(dialog);
        return dialog;
    }

    private static Task Press(FileViewerDialog dialog, ConsoleKey key) =>
        dialog.HandleKeyEventAsync(new KeyEvent('\0', key, false, false, false));

    private static long Cursor(FileViewerDialog dialog) =>
        dialog.Controls.OfType<iConsole.Controls.Viewers.TextViewerControl>().First().ScrollableModel.CursorPosition.Y;

    [TestMethod]
    public async Task DownMovesTheCursorWhenTheKeyGoesToTheDialog()
    {
        FileViewerDialog dialog = await OpenAsync(300);

        var viewer = dialog.Controls.OfType<iConsole.Controls.Viewers.TextViewerControl>().First();
        Assert.IsFalse(dialog.HexViewer.IsFocused, "the hidden hex dump does not hold the focus");
        await Press(dialog, ConsoleKey.DownArrow);
        Assert.AreEqual(1, viewer.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task TheHexDumpTakesTheKeysWhileItIsShown_AndGivesThemBack()
    {
        FileViewerDialog dialog = await OpenAsync(300);
        var viewer = dialog.Controls.OfType<iConsole.Controls.Viewers.TextViewerControl>().First();

        await dialog.ToggleHexModeAsync();
        Assert.IsTrue(dialog.HexViewer.IsFocused);
        await dialog.ToggleHexModeAsync();

        Assert.IsFalse(dialog.HexViewer.IsFocused);
        await Press(dialog, ConsoleKey.DownArrow);
        Assert.AreEqual(1, viewer.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task PageDownAndEndWorkThroughTheDialogToo()
    {
        FileViewerDialog dialog = await OpenAsync(300);

        await Press(dialog, ConsoleKey.PageDown);
        Assert.IsTrue(Cursor(dialog) > 10, "PageDown moved by a screen");
        await Press(dialog, ConsoleKey.End);
        Assert.AreEqual(299, Cursor(dialog));
    }
}
