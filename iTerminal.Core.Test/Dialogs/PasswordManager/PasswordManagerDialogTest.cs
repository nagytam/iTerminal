using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs.PasswordManager;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using Microsoft.EntityFrameworkCore;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.PasswordManager;

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
}
