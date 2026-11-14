using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls.Editors;

/// <summary>F3 finds the next match and Ctrl+F3 the previous one, in the editor and in the F3 viewer, from the keyboard.</summary>
[TestClass]
public class FindKeysTest
{
    private static FileViewerDialog NewViewer(params string[] lines)
    {
        var read = new Mock<IFileToTextConverterService>();
        read.Setup(f => f.ReadFileAsync(It.IsAny<Url>(), It.IsAny<IFileSystem>(), It.IsAny<TextModel>()))
            .Returns((Url? _url, IFileSystem _fileSystem, TextModel model) =>
            {
                foreach (var line in lines)
                {
                    model.Lines.Add(line);
                }

                return Task.CompletedTask;
            });
        return new FileViewerDialog(
            Mock.Of<IContentModelService>(),
            read.Object,
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            new ThemeService());
    }

    private static TextFileEditorDialog NewEditor(params string[] lines)
    {
        var dialog = new TextFileEditorDialog(
            Mock.Of<IContentModelService>(),
            Mock.Of<IFileToTextConverterService>(),
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            new ThemeService());
        foreach (var line in lines)
        {
            dialog.TextEditorControl.TextModel.Lines.Add(line);
        }

        return dialog;
    }

    private static KeyEvent F3(bool control) => new('\0', ConsoleKey.F3, false, false, control);

    [Timeout(60_000)]
    [TestMethod]
    public async Task Viewer_F3AndCtrlF3_MoveToTheNextAndThePreviousMatch()
    {
        var dialog = NewViewer("a x", "b", "x c", "d x");
        await dialog.OnInitializeAsync();
        var viewer = dialog.TextViewerControl;
        viewer.SearchModel.Hits.Add(new Position(2, 0));
        viewer.SearchModel.Hits.Add(new Position(0, 2));
        viewer.SearchModel.Hits.Add(new Position(2, 3));
        viewer.ScrollableModel.CursorPosition.Y = 1;
        viewer.ScrollableModel.CursorPosition.X = 0;

        await dialog.HandleKeyEventAsync(F3(control: false));
        Assert.AreEqual(2L, viewer.ScrollableModel.CursorPosition.Y, "F3 goes to the match after the cursor");

        await dialog.HandleKeyEventAsync(F3(control: true));
        Assert.AreEqual(0L, viewer.ScrollableModel.CursorPosition.Y, "Ctrl+F3 goes back to the match before it");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Editor_F3AndCtrlF3_MoveToTheNextAndThePreviousMatch()
    {
        var dialog = NewEditor("a x", "b", "x c", "d x");
        var editor = dialog.TextEditorControl;
        editor.SearchModel.Hits.Add(new Position(2, 0));
        editor.SearchModel.Hits.Add(new Position(0, 2));
        editor.SearchModel.Hits.Add(new Position(2, 3));
        editor.ScrollableModel.CursorPosition.Y = 1;
        editor.ScrollableModel.CursorPosition.X = 0;

        await dialog.HandleKeyEventAsync(F3(control: false));
        Assert.AreEqual(2L, editor.ScrollableModel.CursorPosition.Y, "F3 goes to the match after the cursor");

        await dialog.HandleKeyEventAsync(F3(control: true));
        Assert.AreEqual(0L, editor.ScrollableModel.CursorPosition.Y, "Ctrl+F3 goes back to the match before it");
    }
}
