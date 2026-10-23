using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Services.UserMenu;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>Shift+F10: the user menu — choosing with the cursor or an entry's own key, running, failing, and editing the menu file.</summary>
[TestClass]
public class UserMenuDialogTest
{
    private static readonly UserMenuContext Context = new("notes.txt", @"C:\work", @"C:\other", []);

    private static UserMenuItem Item(char hotkey, string title) => new(hotkey, title, "*", [title.ToLowerInvariant()]);

    private sealed record Fixture(
        UserMenuDialog Dialog,
        Control Root,
        Mock<IUserMenuService> Menu,
        Mock<IFileLauncher> Launcher,
        Mock<INotificationService> Notifications);

    private static async Task<Fixture> OpenAsync(IReadOnlyList<UserMenuItem> items, Size? screen = null, string? failure = null)
    {
        Mock<IUserMenuService> menu = new();
        menu.SetupGet(m => m.FilePath).Returns(@"C:\data\usermenu.txt");
        menu.Setup(m => m.GetItems(It.IsAny<string?>())).Returns(items);
        menu.Setup(m => m.RunAsync(It.IsAny<UserMenuItem>(), It.IsAny<UserMenuContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(failure);
        Mock<IFileLauncher> launcher = new();
        launcher.Setup(l => l.OpenWithSystemDefaultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(default(LaunchOutcome)!);
        Mock<INotificationService> notifications = new();
        UserMenuDialog dialog = new(menu.Object, launcher.Object, TestTheme(), notifications.Object);
        dialog.Load(Context);
        Control root = await ShowAsync(dialog, screen);
        return new Fixture(dialog, root, menu, launcher, notifications);
    }

    private static Task PressAsync(Fixture f, ConsoleKey key, char c = '\0') => f.Dialog.HandleKeyEventAsync(Key(key, c));

    private static readonly UserMenuItem[] Three = [Item('a', "Archive"), Item('b', "Build"), Item('c', "Clean")];

    [TestMethod]
    public async Task ItListsTheEntriesWithTheirKeys_AndTheCursorOnTheFirst()
    {
        Fixture f = await OpenAsync(Three);

        AssertScreenContains(Render(f.Root), "▶ a  Archive");
        AssertScreenContains(Render(f.Root), "  b  Build");
        AssertScreenContains(Render(f.Root), "  c  Clean");
        AssertScreenContains(Render(f.Root), "Enter:run");
        Assert.AreEqual("Archive", f.Dialog.Selected!.Title);
        f.Menu.Verify(m => m.GetItems("notes.txt"), Times.Once);
    }

    [TestMethod]
    public async Task TheArrowsHomeAndEnd_MoveTheCursorAndStopAtTheEnds()
    {
        Fixture f = await OpenAsync(Three);

        await PressAsync(f, ConsoleKey.UpArrow);
        Assert.AreEqual("Archive", f.Dialog.Selected!.Title);
        await PressAsync(f, ConsoleKey.DownArrow);
        await PressAsync(f, ConsoleKey.DownArrow);
        await PressAsync(f, ConsoleKey.DownArrow);
        Assert.AreEqual("Clean", f.Dialog.Selected!.Title);
        await PressAsync(f, ConsoleKey.Home);
        Assert.AreEqual("Archive", f.Dialog.Selected!.Title);
        await PressAsync(f, ConsoleKey.End);
        Assert.AreEqual("Clean", f.Dialog.Selected!.Title);
        AssertScreenContains(Render(f.Root), "▶ c  Clean");
    }

    [TestMethod]
    public async Task Enter_RunsTheSelectedEntryWithTheContext_AndClosesTheMenu()
    {
        Fixture f = await OpenAsync(Three);
        await PressAsync(f, ConsoleKey.DownArrow);

        await PressAsync(f, ConsoleKey.Enter);
        await f.Dialog.LastRun;

        f.Menu.Verify(m => m.RunAsync(Three[1], Context, It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsFalse(f.Dialog.IsVisible);
        f.Notifications.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task AnEntrysOwnKey_RunsItAtOnce_WhereverTheCursorIs()
    {
        Fixture f = await OpenAsync(Three);

        await PressAsync(f, ConsoleKey.C, 'c');
        await f.Dialog.LastRun;

        f.Menu.Verify(m => m.RunAsync(Three[2], Context, It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsFalse(f.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task AKeyNoEntryHas_IsIgnored()
    {
        Fixture f = await OpenAsync(Three);

        await PressAsync(f, ConsoleKey.Z, 'z');

        f.Menu.Verify(m => m.RunAsync(It.IsAny<UserMenuItem>(), It.IsAny<UserMenuContext>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(f.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task AFailedRun_IsReportedAsAnErrorNotification()
    {
        Fixture f = await OpenAsync(Three, failure: "no shell");

        await PressAsync(f, ConsoleKey.Enter);
        await f.Dialog.LastRun;

        f.Notifications.Verify(n => n.Notify("Archive", "no shell", NotificationSeverity.Error, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task F4_OpensTheMenuFileForEditing_AndClosesTheMenu()
    {
        Fixture f = await OpenAsync(Three);

        await PressAsync(f, ConsoleKey.F4);
        await f.Dialog.LastRun;

        f.Launcher.Verify(l => l.OpenWithSystemDefaultAsync(@"C:\data\usermenu.txt"), Times.Once);
        Assert.IsFalse(f.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task WithNoEntries_ItSaysWhereToAddSome_AndEnterDoesNothing()
    {
        Fixture f = await OpenAsync([]);

        AssertScreenContains(Render(f.Root), "no entries - F4 edits usermenu.txt");
        Assert.IsNull(f.Dialog.Selected);
        await PressAsync(f, ConsoleKey.Enter);
        await PressAsync(f, ConsoleKey.DownArrow);
        await PressAsync(f, ConsoleKey.End);
        f.Menu.Verify(m => m.RunAsync(It.IsAny<UserMenuItem>(), It.IsAny<UserMenuContext>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.IsTrue(f.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task KeysWithControlOrAlt_AreNotTheMenusOwn()
    {
        Fixture f = await OpenAsync(Three);

        await f.Dialog.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, false, true));
        await f.Dialog.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, true, false));

        f.Menu.Verify(m => m.RunAsync(It.IsAny<UserMenuItem>(), It.IsAny<UserMenuContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AnEntryWithoutAKey_IsStillReachableWithTheCursor()
    {
        UserMenuItem[] items = [Item(' ', "No key"), Item('b', "Build")];
        Fixture f = await OpenAsync(items);

        AssertScreenContains(Render(f.Root), "▶    No key");
        await PressAsync(f, ConsoleKey.Spacebar, ' ');
        f.Menu.Verify(m => m.RunAsync(It.IsAny<UserMenuItem>(), It.IsAny<UserMenuContext>(), It.IsAny<CancellationToken>()), Times.Never);
        await PressAsync(f, ConsoleKey.Enter);
        await f.Dialog.LastRun;
        f.Menu.Verify(m => m.RunAsync(items[0], Context, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ALongMenu_ScrollsOnlyWhenTheCursorLeavesTheWindow()
    {
        UserMenuItem[] many = Enumerable.Range(0, 30).Select(i => Item((char)('a' + i % 26), "Entry " + i.ToString("00"))).ToArray();
        Fixture f = await OpenAsync(many, screen: new Size(60, 12));

        await PressAsync(f, ConsoleKey.End);
        AssertScreenContains(Render(f.Root), "Entry 29");
        AssertScreenDoesNotContain(Render(f.Root), "Entry 00");

        // One Up inside the window moves the cursor, not the list.
        await PressAsync(f, ConsoleKey.UpArrow);
        AssertScreenContains(Render(f.Root), "▶ " + many[28].Hotkey + "  Entry 28");
        AssertScreenContains(Render(f.Root), "Entry 29");

        await PressAsync(f, ConsoleKey.Home);
        AssertScreenContains(Render(f.Root), "Entry 00");
        AssertScreenDoesNotContain(Render(f.Root), "Entry 29");
    }

    [TestMethod]
    public async Task Escape_ClosesTheMenuWithoutRunningAnything()
    {
        Fixture f = await OpenAsync(Three);

        await PressAsync(f, ConsoleKey.Escape);

        Assert.IsFalse(f.Dialog.IsVisible);
        f.Menu.Verify(m => m.RunAsync(It.IsAny<UserMenuItem>(), It.IsAny<UserMenuContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
