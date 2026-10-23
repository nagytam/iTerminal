using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs.PasswordManager;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using Microsoft.EntityFrameworkCore;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.PasswordManager;

// Some tests put text on the ambient clipboard and paste it back, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class PasswordManagerDialogTest
{
    private sealed class SqliteDbContextFactory(string databasePath) : IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options = new DbContextOptionsBuilder<iTerminalDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        public iTerminalDbContext CreateDbContext() => new(_options);

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private static iTerminal.Core.Services.PasswordManager NewManager() => new(
        Path.Combine(Path.GetTempPath(), $"iTerminal.PmDialog.{Guid.NewGuid():N}.sqlite"),
        Path.Combine(Path.GetTempPath(), $"iTerminal.PmDialogKeys.{Guid.NewGuid():N}"));

    private static async Task TypeAsync(PasswordManagerDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    [TestMethod]
    public async Task TheStoreOpensItselfWithoutAskingForAPassword()
    {
        var dialog = new PasswordManagerDialog(NewManager(), TestTheme());

        await ShowAsync(dialog);

        Assert.AreEqual("List", dialog.ModeForTest);
    }

    [TestMethod]
    public async Task ALockedStoreIsOpenedAgainWhenTheDialogShows()
    {
        var manager = NewManager();
        manager.Unlock();
        manager.Lock();
        var dialog = new PasswordManagerDialog(manager, TestTheme());

        await ShowAsync(dialog);

        Assert.AreEqual("List", dialog.ModeForTest);
        Assert.IsTrue(manager.IsUnlocked);
    }

    [TestMethod]
    public async Task AStoreWhoseKeyIsLostSaysSo()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"iTerminal.PmDialog.{Guid.NewGuid():N}.sqlite");
        new iTerminal.Core.Services.PasswordManager(databasePath, Path.Combine(Path.GetTempPath(), $"iTerminal.PmDialogKeys.{Guid.NewGuid():N}")).Unlock();
        var other = new iTerminal.Core.Services.PasswordManager(databasePath, Path.Combine(Path.GetTempPath(), $"iTerminal.PmDialogKeys.{Guid.NewGuid():N}"));
        var dialog = new PasswordManagerDialog(other, TestTheme());

        await ShowAsync(dialog);

        Assert.AreEqual("Unavailable", dialog.ModeForTest);
        AssertScreenContains(Render(dialog), "cannot be opened");
    }

    [TestMethod]
    public async Task ANewPasswordIsSavedWithItsFields()
    {
        var manager = NewManager();
        manager.Unlock();
        var dialog = new PasswordManagerDialog(manager, TestTheme());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "Bank");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        await TypeAsync(dialog, "ada");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));
        await TypeAsync(dialog, "s3cret!");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.S, 's', control: true));

        var saved = manager.List().Single();
        Assert.AreEqual("Bank", saved.Title);
        Assert.AreEqual("ada", saved.Username);
        Assert.AreEqual("s3cret!", saved.Password);
        Assert.AreEqual("List", dialog.ModeForTest);
    }

    [TestMethod]
    public async Task ThePasswordStaysHiddenUntilVShowsIt()
    {
        var manager = NewManager();
        manager.Unlock();
        manager.Save(new PasswordEntry { Id = "p1", Title = "Mail", Password = "visible-when-asked" });
        var dialog = new PasswordManagerDialog(manager, TestTheme());
        await ShowAsync(dialog);

        AssertScreenDoesNotContain(Render(dialog), "visible-when-asked");

        await dialog.HandleKeyEventAsync(Char('v'));

        Assert.IsTrue(dialog.RevealForTest);
        AssertScreenContains(Render(dialog), "visible-when-asked");
    }

    [TestMethod]
    public async Task DeleteNeedsAYesAndAnyOtherKeyCancelsIt()
    {
        var manager = NewManager();
        manager.Unlock();
        manager.Save(new PasswordEntry { Id = "p1", Title = "Keep me", Password = "x" });
        var dialog = new PasswordManagerDialog(manager, TestTheme());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('d'));
        await dialog.HandleKeyEventAsync(Char('q'));
        Assert.AreEqual(1, manager.List().Count);
        Assert.AreEqual("List", dialog.ModeForTest);

        await dialog.HandleKeyEventAsync(Char('d'));
        await dialog.HandleKeyEventAsync(Char('y'));
        Assert.AreEqual(0, manager.List().Count);
    }

    [TestMethod]
    public async Task CtrlVPastesIntoTheFieldUnderTheCursor_OnOneLine_AndNotIntoTheCategory()
    {
        var manager = NewManager();
        manager.Unlock();
        var dialog = new PasswordManagerDialog(manager, TestTheme());
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('n'));
        await iConsole.Services.TextClipboard.Current.SetTextAsync("Bank" + (char)10 + "login");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.V, 'v', control: true));
        Assert.AreEqual("Bank login", dialog.FieldForTest("Title"));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, (char)9));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, (char)9));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.V, 'v', control: true));
        Assert.AreEqual(string.Empty, dialog.FieldForTest("Username"));
        Assert.AreEqual(string.Empty, dialog.FieldForTest("Password"));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, (char)9));
        await iConsole.Services.TextClipboard.Current.SetTextAsync("p4ss w0rd");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Insert, shift: true));
        Assert.AreEqual("p4ss w0rd", dialog.FieldForTest("Password"));
    }

    [TestMethod]
    public async Task CtrlUClearsTheFieldUnderTheCursor()
    {
        var manager = NewManager();
        manager.Unlock();
        var dialog = new PasswordManagerDialog(manager, TestTheme());
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "Bank");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.U, 'u', control: true));

        Assert.AreEqual(string.Empty, dialog.FieldForTest("Title"));
    }

    [TestMethod]
    public async Task ShiftTabGoesBackAFieldAndWraps()
    {
        var manager = NewManager();
        manager.Unlock();
        var dialog = new PasswordManagerDialog(manager, TestTheme());
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('n'));

        // From the first field back to the last (Notes): typing lands there.
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, (char)9, shift: true));
        await TypeAsync(dialog, "memo");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Tab, (char)9, shift: true));
        await TypeAsync(dialog, "pw");

        Assert.AreEqual("memo", dialog.FieldForTest("Notes"));
        Assert.AreEqual("pw", dialog.FieldForTest("Password"));
        Assert.AreEqual(string.Empty, dialog.FieldForTest("Title"));
    }

    // The list starts after the border, the heading and the column header: three rows down, two columns in.
    private static iConsole.Data.MouseEvent At(iConsole.Controls.Control dialog, int row, int column = 8, int wheel = 0, bool click = true) => new()
    {
        Position = new iConsole.Data.Position(dialog.AbsolutePosition.X + 2 + column, dialog.AbsolutePosition.Y + 3 + row),
        LeftClicked = click && wheel == 0,
        WheelDelta = wheel,
    };

    private static async Task<(PasswordManagerDialog Dialog, iTerminal.Core.Services.PasswordManager Manager)> OpenWithAsync(int count)
    {
        var manager = NewManager();
        manager.Unlock();
        for (int i = 0; i < count; i++)
        {
            manager.Save(new PasswordEntry { Id = "p" + i, Title = "Entry " + i.ToString("00"), Password = "x" });
        }

        var dialog = new PasswordManagerDialog(manager, TestTheme());
        await ShowAsync(dialog);
        return (dialog, manager);
    }

    [TestMethod]
    public async Task AClickHighlightsAnEntry_AndTheWheelMovesThreeAtATime()
    {
        var (dialog, _) = await OpenWithAsync(10);
        int start = dialog.CursorForTest;

        Assert.IsTrue(await dialog.HandleMouseEventAsync(At(dialog, 4)));
        Assert.AreEqual(4, dialog.CursorForTest);

        await dialog.HandleMouseEventAsync(At(dialog, 0, wheel: -1));
        Assert.AreEqual(7, dialog.CursorForTest);
        await dialog.HandleMouseEventAsync(At(dialog, 0, wheel: 1));
        Assert.AreEqual(4, dialog.CursorForTest);
        Assert.AreNotEqual(start, dialog.CursorForTest);
    }

    [TestMethod]
    public async Task AClickBelowTheLastEntryOrOutsideTheDialogDoesNothing()
    {
        var (dialog, _) = await OpenWithAsync(3);

        await dialog.HandleMouseEventAsync(At(dialog, 9));
        Assert.IsFalse(await dialog.HandleMouseEventAsync(new iConsole.Data.MouseEvent { Position = new iConsole.Data.Position(0, 0), WheelDelta = -1 }));

        Assert.AreEqual(0, dialog.CursorForTest);
    }

    [TestMethod]
    public async Task TheMouseLeavesTheListAloneWhileDeletionIsAsked()
    {
        var (dialog, manager) = await OpenWithAsync(5);
        await dialog.HandleKeyEventAsync(Char('d'));

        await dialog.HandleMouseEventAsync(At(dialog, 3));
        Assert.AreEqual(0, dialog.CursorForTest);
        await dialog.HandleKeyEventAsync(Char('y'));

        Assert.AreEqual(4, manager.List().Count);
    }
}
