using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.StatusBar;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Viewers;

/// <summary>The viewer's wrap, go to line and search hit navigation.</summary>
[TestClass]
public class TextViewerNavigationTest
{
    private static TextViewerControl Viewer(params string[] lines)
    {
        var viewer = new TextViewerControl(false, new ThemeService());
        foreach (string line in lines)
        {
            viewer.TextModel.Lines.Add(line);
        }

        return viewer;
    }

    private static TextViewerControl Show(TextViewerControl viewer, int width, int height)
    {
        viewer.ViewSize = new Size(width, height);
        return viewer;
    }

    private static KeyEvent Key(ConsoleKey consoleKey) => new('\r', consoleKey, false, false, false);

    /// <summary>Puts text in the dialog's line box, as if typed over the number it starts with.</summary>
    private static void SetLine(GoToLineDialog dialog, string text) =>
        dialog.Controls.OfType<TextBox>().Single().Text = text;

    [TestMethod]
    public void WithoutWrapping_ALongLineRunsOffTheRightEdge()
    {
        var viewer = Show(Viewer("abcdefghijklmnopqrstuvwxyz"), 12, 4);

        var screen = ScreenBuffer.Render(viewer, 12, 4);

        Assert.IsTrue(screen.Contains("abcdefghij"), screen.ToString());
        Assert.IsFalse(screen.Contains("klmno"), screen.ToString());
    }

    [TestMethod]
    public void WithWrapping_ALongLineContinuesOnTheNextRows()
    {
        var viewer = Show(Viewer("abcdefghijklmnopqrstuvwxyz", "short"), 12, 6);
        viewer.WrapLines = true;

        var screen = ScreenBuffer.Render(viewer, 12, 6);

        Assert.IsTrue(screen.Contains("abcdefghij"), screen.ToString());
        Assert.IsTrue(screen.Contains("klmnopqrst"), screen.ToString());
        Assert.IsTrue(screen.Contains("uvwxyz"), screen.ToString());
        Assert.IsTrue(screen.Contains("short"), screen.ToString());
    }

    [TestMethod]
    public void WrappedRows_AreCountedForTheScroll()
    {
        var viewer = Show(Viewer("abcdefghijklmnopqrstuvwxyz", "b"), 12, 6);
        viewer.WrapLines = true;
        ScreenBuffer.Render(viewer, 12, 6);

        Assert.AreEqual(4, viewer.ScrollableModel.ScrollableSize.Height);
    }

    [TestMethod]
    public void ShowLine_BringsTheLineOntoTheScreen()
    {
        var viewer = Show(Viewer(Enumerable.Range(1, 100).Select(i => $"line {i:000}").ToArray()), 20, 6);
        ScreenBuffer.Render(viewer, 20, 6);

        viewer.ShowLine(80, 0);
        var screen = ScreenBuffer.Render(viewer, 20, 6);

        Assert.IsTrue(screen.Contains("line 081"), screen.ToString());
        Assert.AreEqual(81, viewer.CursorLine);
    }

    [TestMethod]
    public void ShowLine_WithWrapping_ReachesTheRowOfTheColumn()
    {
        var viewer = Show(Viewer(Enumerable.Range(1, 50).Select(i => $"row {i} " + new string('x', 30)).ToArray()), 12, 5);
        viewer.WrapLines = true;
        ScreenBuffer.Render(viewer, 12, 5);

        viewer.ShowLine(40, 25);
        ScreenBuffer.Render(viewer, 12, 5);

        // The column is on a later row of its line, so the view has scrolled past the line's first row.
        Assert.IsTrue(viewer.ScrollableModel.ScrollPosition.Y > 0);
        Assert.AreEqual(41, viewer.CursorLine);
    }

    [TestMethod]
    public void WrapCommand_TogglesWrappingAndKeepsTheCursorLine()
    {
        var viewer = Show(Viewer(Enumerable.Range(1, 40).Select(i => $"line {i} " + new string('y', 40)).ToArray()), 20, 6);
        ScreenBuffer.Render(viewer, 20, 6);
        viewer.ShowLine(30, 0);
        var command = new ViewerWrapLinesCommand(viewer);

        Assert.IsTrue(command.CanExecute());
        command.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.IsTrue(viewer.WrapLines);
        Assert.AreEqual(31, viewer.CursorLine);
    }

    [TestMethod]
    public void WrapCommand_IsNotAvailableInTheHexView()
    {
        var viewer = Viewer("text");
        viewer.IsVisible = false;

        Assert.IsFalse(new ViewerWrapLinesCommand(viewer).CanExecute());
    }

    [TestMethod]
    public void HitCounter_NamesTheHitTheCursorIsOn()
    {
        var viewer = Show(Viewer("alpha beta alpha", "gamma alpha"), 40, 4);
        viewer.SearchModel.Search = "alpha";
        viewer.SearchModel.Hits.Add(new Position(0, 0));
        viewer.SearchModel.Hits.Add(new Position(11, 0));
        viewer.SearchModel.Hits.Add(new Position(6, 1));
        var searchModel = viewer.SearchModel;
        var indicator = new SearchResultsIndicator(searchModel, new ThemeService(), viewer.ScrollableModel);

        viewer.ShowLine(0, 11);
        Assert.AreEqual("Hit 2/3", indicator.HitText());

        viewer.ShowLine(0, 5);
        Assert.AreEqual("Hit 1/3", indicator.HitText(), "between two hits, the one before");
    }

    [TestMethod]
    public void NextHit_RevealsItThroughTheViewer()
    {
        var viewer = Show(Viewer(Enumerable.Range(1, 60).Select(i => i == 45 ? "needle here" : $"hay {i}").ToArray()), 30, 5);
        ScreenBuffer.Render(viewer, 30, 5);
        viewer.SearchModel.Search = "needle";
        viewer.SearchModel.Hits.Add(new Position(0, 44));
        var next = new FindNextCommand(viewer.SearchModel, viewer.ScrollableModel, viewer.ShowLine);

        next.ExecuteAsync(null).GetAwaiter().GetResult();
        var screen = ScreenBuffer.Render(viewer, 30, 5);

        Assert.AreEqual(45, viewer.CursorLine);
        Assert.IsTrue(screen.Contains("needle here"), screen.ToString());
    }

    [TestMethod]
    public async Task GoToLine_GoesToTheLineTyped()
    {
        var viewer = Show(Viewer(Enumerable.Range(1, 200).Select(i => $"line {i}").ToArray()), 30, 6);
        ScreenBuffer.Render(viewer, 30, 6);
        var dialog = new GoToLineDialog(viewer, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), new ThemeService());
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();
        SetLine(dialog, "142");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(142, viewer.CursorLine);
    }

    [TestMethod]
    public async Task GoToLine_PastTheEnd_GoesToTheLastLine()
    {
        var viewer = Show(Viewer("one", "two", "three"), 30, 6);
        var dialog = new GoToLineDialog(viewer, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), new ThemeService());
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();
        SetLine(dialog, "99");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(3, viewer.CursorLine);
    }

    [TestMethod]
    public async Task GoToLine_NotANumber_SaysSoAndStaysPut()
    {
        var viewer = Show(Viewer("one", "two", "three"), 30, 6);
        viewer.ShowLine(1, 0);
        var dialog = new GoToLineDialog(viewer, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), new ThemeService());
        await dialog.OnInitializeAsync();
        await dialog.ShowAsync();
        SetLine(dialog, "two");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual("Enter a line number.", dialog.Message);
        Assert.AreEqual(2, viewer.CursorLine);
    }

    [TestMethod]
    public void GoToLineCommand_IsNotAvailableInTheHexView()
    {
        var viewer = Viewer("text");
        viewer.IsVisible = false;

        Assert.IsFalse(new ViewerGoToLineCommand(viewer, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), new ThemeService()).CanExecute());
    }
}
