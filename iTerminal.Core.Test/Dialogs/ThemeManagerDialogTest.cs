using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Alt+F6: pick a colour scheme and apply it.</summary>
[TestClass]
public class ThemeManagerDialogTest
{
    [TestMethod]
    public async Task ListsEveryThemeAndMarksTheCurrentOne()
    {
        var themeService = new ThemeService();
        var dialog = new ThemeManagerDialog(themeService);
        await ShowAsync(dialog);

        var screen = Render(dialog);

        AssertScreenContains(screen, "● Default");
        AssertScreenContains(screen, "  Dark");
        AssertScreenContains(screen, "Enter:apply  Esc:close");
    }

    [TestMethod]
    public async Task EnterAppliesTheThemeUnderTheCursorAndKeepsTheDialogOpen()
    {
        // Enter used to tick the row and nothing else: no theme was ever applied from here.
        var themeService = new ThemeService();
        var dialog = new ThemeManagerDialog(themeService);
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual(Theme.Dark, themeService.Theme);
        Assert.AreEqual("Dark theme applied.", dialog.StatusForTest);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "the dialog stays for trying another");
        AssertScreenContains(Render(dialog), "● Dark");
    }

    [TestMethod]
    public async Task TheCursorStartsOnTheThemeInUse()
    {
        var themeService = new ThemeService { Theme = Theme.Dark };
        var dialog = new ThemeManagerDialog(themeService);
        await ShowAsync(dialog);

        Assert.AreEqual(1, (int)dialog.ListBoxForTest.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task AServiceThatCannotSwitchSaysSo()
    {
        var dialog = new ThemeManagerDialog(TestTheme());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        StringAssert.Contains(dialog.StatusForTest, "cannot be applied");
    }

    [TestMethod]
    public async Task LayingOutAgainDoesNotStackAnotherList()
    {
        // A new list box was built and added on every layout pass — every resize, every reopening.
        var dialog = new ThemeManagerDialog(new ThemeService());
        await ShowAsync(dialog);

        dialog.OnInitializeLayout();
        dialog.OnInitializeLayout();

        Assert.AreEqual(1, dialog.Controls.AsSnapshot().OfType<ListBox<iConsole.Display.ColorScheme>>().Count());
    }

    [TestMethod]
    public async Task TheListIsSizedFromTheDialogItSitsIn()
    {
        var dialog = new ThemeManagerDialog(new ThemeService());
        await ShowAsync(dialog);

        var list = dialog.ListBoxForTest;

        Assert.AreEqual(dialog.ViewSize.Width - 2, list.ViewSize.Width);
        Assert.IsTrue(list.ViewSize.Height >= 2, "room for both themes");
        Assert.IsTrue(list.Position.Y + list.ViewSize.Height < dialog.ViewSize.Height - 1, "the hint row stays free");
    }

    [TestMethod]
    public async Task EscapeClosesTheDialog()
    {
        var dialog = new ThemeManagerDialog(new ThemeService());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }
}
