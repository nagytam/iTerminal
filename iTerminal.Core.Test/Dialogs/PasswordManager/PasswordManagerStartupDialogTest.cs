using iConsole.Data;
using iConsole.Services;
using Moq;
using iTerminal.Core.Dialogs.PasswordManager;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.PasswordManager;

[TestClass]
public class PasswordManagerStartupDialogTest
{
    private const string Master = "correct horse battery";

    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"iTerminal.PmStartup.{Guid.NewGuid():N}.sqlite");

    private static string NewKeyDirectory() => Path.Combine(Path.GetTempPath(), $"iTerminal.PmStartupKeys.{Guid.NewGuid():N}");

    private static iTerminal.Core.Services.PasswordManager NewManager() => new(NewDatabasePath(), NewKeyDirectory());

    private sealed class Count
    {
        public int Value { get; set; }
    }

    private static PasswordManagerStartupDialog NewDialog(iTerminal.Core.Services.PasswordManager manager, out Count stopped, out Count unlocked)
    {
        var dialog = new PasswordManagerStartupDialog(
            manager,
            new Mock<IBeepService>().Object,
            new Mock<IDateTimeProviderService>().Object,
            TestTheme());
        var stops = new Count();
        var unlocks = new Count();
        dialog.OnStopped = () => stops.Value++;
        dialog.OnUnlocked = () => unlocks.Value++;
        stopped = stops;
        unlocked = unlocks;
        return dialog;
    }

    [TestMethod]
    public async Task ALockedStoreAsksForTheMasterPassword()
    {
        var manager = NewManager();
        manager.Setup(Master);
        manager.Lock();
        var dialog = NewDialog(manager, out _, out _);

        await ShowAsync(dialog);

        Assert.IsFalse(dialog.IsUnlocked);
        AssertScreenContains(Render(dialog), "Master password");
        AssertScreenContains(Render(dialog), "Remember on this computer for 30 days");
    }

    [TestMethod]
    public async Task TheRightPasswordUnlocksAndSaysSo()
    {
        var manager = NewManager();
        manager.Setup(Master);
        manager.Lock();
        var dialog = NewDialog(manager, out _, out Count unlocked);
        await ShowAsync(dialog);

        await TypeAsync(dialog, Master);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.IsTrue(manager.IsUnlocked);
        Assert.AreEqual(1, unlocked.Value);
    }

    [TestMethod]
    public async Task ThreeWrongPasswordsStopTheApp()
    {
        var manager = NewManager();
        manager.Setup(Master);
        manager.Lock();
        var dialog = NewDialog(manager, out Count stopped, out _);
        await ShowAsync(dialog);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await TypeAsync(dialog, "wrong password");
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        }

        Assert.IsFalse(manager.IsUnlocked);
        Assert.AreEqual(1, stopped.Value);
    }

    [TestMethod]
    public async Task EscStopsTheAppWithoutUnlocking()
    {
        var manager = NewManager();
        manager.Setup(Master);
        manager.Lock();
        var dialog = NewDialog(manager, out Count stopped, out _);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(manager.IsUnlocked);
        Assert.AreEqual(1, stopped.Value);
    }

    [TestMethod]
    public async Task F10QuitsTheAppLikeEscDoes()
    {
        // The dialog is modal, so a key it declines never reaches the screen's Quit: F10 has to quit here too.
        var manager = NewManager();
        manager.Setup(Master);
        manager.Lock();
        var dialog = NewDialog(manager, out Count stopped, out _);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F10));

        Assert.IsFalse(manager.IsUnlocked);
        Assert.AreEqual(1, stopped.Value);
    }

    [TestMethod]
    public async Task CtrlF10QuitsTheAppToo()
    {
        var manager = NewManager();
        manager.Setup(Master);
        manager.Lock();
        var dialog = NewDialog(manager, out Count stopped, out _);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.F10, control: true));

        Assert.IsFalse(manager.IsUnlocked);
        Assert.AreEqual(1, stopped.Value);
    }

    [TestMethod]
    public async Task TickingRememberKeepsTheStoreOpenForTheNextStart()
    {
        var databasePath = NewDatabasePath();
        var keyDirectory = NewKeyDirectory();
        var manager = new iTerminal.Core.Services.PasswordManager(databasePath, keyDirectory);
        manager.Setup(Master);
        manager.Lock();
        var dialog = NewDialog(manager, out _, out Count unlocked);
        await ShowAsync(dialog);

        dialog.RememberForTest.IsChecked = true;
        await TypeAsync(dialog, Master);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(1, unlocked.Value);
        Assert.IsTrue(new iTerminal.Core.Services.PasswordManager(databasePath, keyDirectory).UnlockRemembered());
    }

    [TestMethod]
    public async Task WithoutTheBoxTheNextStartAsksAgain()
    {
        var databasePath = NewDatabasePath();
        var keyDirectory = NewKeyDirectory();
        var manager = new iTerminal.Core.Services.PasswordManager(databasePath, keyDirectory);
        manager.Setup(Master);
        manager.Lock();
        var dialog = NewDialog(manager, out _, out Count unlocked);
        await ShowAsync(dialog);

        await TypeAsync(dialog, Master);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(1, unlocked.Value);
        Assert.IsFalse(new iTerminal.Core.Services.PasswordManager(databasePath, keyDirectory).UnlockRemembered());
    }
}
