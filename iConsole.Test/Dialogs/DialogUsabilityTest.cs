using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Dialogs;

/// <summary>What the small input dialogs tell the person: each message is on screen, not only in a property.</summary>
[TestClass]
public class DialogUsabilityTest
{
    private static KeyEvent Enter => new('\r', ConsoleKey.Enter, false, false, false);

    private static TextViewerControl Viewer(params string[] lines)
    {
        TextViewerControl viewer = new(false, new ThemeService());
        foreach (string line in lines)
        {
            viewer.TextModel.Lines.Add(line);
        }

        viewer.ViewSize = new Size(30, 6);
        return viewer;
    }

    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GoToLine_NotANumber_PutsTheReasonOnScreen_AndTheRangeInTheLabel()
    {
        GoToLineDialog dialog = new(Viewer("one", "two", "three"), Mock.Of<IBeepService>(), new DateTimeProviderService(), new ThemeService());
        dialog.ViewSize = new Size(44, 5);
        dialog.OnInitializeLayout();
        await dialog.ShowAsync();
        dialog.Controls.OfType<TextBox>().Single().Text = "abc";

        await dialog.HandleKeyEventAsync(Enter);

        string screen = ScreenBuffer.Render(dialog, 44, 5).ToString();
        Assert.IsTrue(screen.Contains("Enter a line number."));
        Assert.IsTrue(screen.Contains("Line (1-3)"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GoToOffset_NotAnOffset_PutsTheReasonOnScreen()
    {
        HexViewer hex = new(new ThemeService()) { Data = new byte[100] };
        hex.ViewSize = new Size(60, 6);
        hex.OnInitializeLayout();
        GoToOffsetDialog dialog = new(hex, Mock.Of<IBeepService>(), new DateTimeProviderService(), new ThemeService());
        dialog.ViewSize = new Size(64, 5);
        dialog.OnInitializeLayout();
        await dialog.ShowAsync();
        dialog.Controls.OfType<TextBox>().Single().Text = "nowhere";

        await dialog.HandleKeyEventAsync(Enter);

        Assert.IsTrue(ScreenBuffer.Render(dialog, 64, 5).ToString().Contains("Enter an offset: a number, or a hex number as 0x1F or 1Fh."));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SearchText_ABadPattern_IsShownOnItsOwnLine_AndTheTitleStaysPut()
    {
        Mock<ISearchModel> search = new();
        SearchTextDialog dialog = new(search.Object, Mock.Of<ITextModel>(), Mock.Of<IScrollableModel>(), Mock.Of<IBeepService>(), new DateTimeProviderService(), new ThemeService());
        dialog.ViewSize = new Size(60, 9);
        dialog.OnInitializeLayout();
        await dialog.ShowAsync();
        string title = dialog.Title;

        dialog.ShowError("Invalid pattern '(open' at offset 5. Not enough )'s.");

        Assert.AreEqual(title, dialog.Title);
        Assert.IsTrue(ScreenBuffer.Render(dialog, 60, 9).ToString().Contains("Not enough )'s."));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReplaceText_NothingFound_SaysSoOnScreen()
    {
        ReplaceTextDialog dialog = new(new TextEditorControl(Theme()), Mock.Of<IBeepService>(), new DateTimeProviderService(), Theme());
        dialog.ViewSize = new Size(64, 11);
        dialog.OnInitializeLayout();
        await dialog.ShowAsync();
        dialog.Controls.OfType<TextBox>().First().Text = "needle";

        dialog.Replace(all: false);

        Assert.AreEqual("Nothing to replace: \"needle\" was not found.", dialog.Message);
        Assert.IsTrue(ScreenBuffer.Render(dialog, 64, 11).ToString().Contains("was not found."));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReplaceText_EmptyFind_AsksForTheText()
    {
        ReplaceTextDialog dialog = new(new TextEditorControl(Theme()), Mock.Of<IBeepService>(), new DateTimeProviderService(), Theme());
        dialog.ViewSize = new Size(64, 11);
        dialog.OnInitializeLayout();
        await dialog.ShowAsync();
        dialog.Controls.OfType<TextBox>().First().Text = string.Empty;

        dialog.Replace(all: true);

        Assert.AreEqual("Type the text to find.", dialog.Message);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_Select_MarksARangeAndPutsTheCursorAtItsEnd()
    {
        TextBox box = new(Mock.Of<IBeepService>(), new DateTimeProviderService(), new ThemeService()) { Text = "report.docx" };

        box.Select(0, 6);

        Assert.IsTrue(box.HasSelection);
        Assert.AreEqual("report", box.SelectedText);
        Assert.AreEqual(6, box.ScrollableModel.CursorPosition.X);

        box.Select(4, 100);
        Assert.AreEqual("rt.docx", box.SelectedText, "a length past the end is cut to the text");
    }
}
