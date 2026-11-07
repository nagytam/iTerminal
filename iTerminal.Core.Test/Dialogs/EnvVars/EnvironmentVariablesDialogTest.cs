using iTerminal.Core.Dialogs.EnvVars;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Services.SystemSettings;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.EnvVars;

[TestClass]
public class EnvironmentVariablesDialogTest
{
    private IDisposable _clipboardScope = null!;

    [TestCleanup]
    public void ReleaseOwnClipboard() => _clipboardScope.Dispose();

    [TestInitialize]
    public void ScopeOwnClipboard() => _clipboardScope = iConsole.Services.TextClipboard.Scope(new iConsole.Services.InProcessTextClipboardService());

    private sealed class MemoryStore : IEnvironmentVariableStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public IReadOnlyList<SystemEnvironmentVariable> Read() =>
            [.. Values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new SystemEnvironmentVariable(p.Key, p.Value))];

        public void Set(string name, string value) => Values[name] = value;

        public void Remove(string name) => Values.Remove(name);
    }

    private sealed class MemorySession : ISessionEnvironment
    {
        public Dictionary<string, string> Values { get; } = [];

        public IReadOnlyList<SystemEnvironmentVariable> Read() =>
            [.. Values.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new SystemEnvironmentVariable(p.Key, p.Value))];

        public SystemSettingResult Set(string name, string value)
        {
            Values[name] = value;
            return SystemSettingResult.Ok($"{name} set");
        }

        public SystemSettingResult Remove(string name)
        {
            Values.Remove(name);
            return SystemSettingResult.Ok($"{name} gone");
        }
    }

    private static (EnvironmentVariablesDialog Dialog, MemoryStore User, MemoryStore Machine, MemorySession Session) Create(bool elevated = true)
    {
        var runner = new Mock<IExternalProcessRunner>();
        var user = new MemoryStore();
        var machine = new MemoryStore();
        var session = new MemorySession();
        var service = new SystemSettingsService(runner.Object, () => elevated, windows: false, linux: true, user, machine);
        return (new EnvironmentVariablesDialog(service, session, TestTheme()), user, machine, session);
    }

    private static async Task OpenAsync(EnvironmentVariablesDialog dialog)
    {
        await ShowAsync(dialog);
        await SettleAsync(dialog);
    }

    /// <summary>Lets the task the dialog started finish, and takes its outcome as the next idle pass would (a change reads again, so twice).</summary>
    private static async Task SettleAsync(EnvironmentVariablesDialog dialog)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            await dialog.PendingOperation;
            await dialog.LoadTask;
            dialog.ApplyParkedForTest();
        }
    }

    private static async Task TypeAsync(EnvironmentVariablesDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    private static Task EnterAsync(EnvironmentVariablesDialog dialog) => dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

    private static Task BackspaceAsync(EnvironmentVariablesDialog dialog) => dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace, '\b'));

    [Timeout(60_000)]
    [TestMethod]
    public async Task ItOpensOnTheUsersVariablesWhenTheSystemKeepsThem()
    {
        var (dialog, user, _, _) = Create();
        user.Values["EDITOR"] = "vim";

        await OpenAsync(dialog);

        Assert.AreEqual(EnvironmentVariablesDialog.EditScope.User, dialog.ScopeForTest);
        Assert.AreEqual(1, dialog.RowCountForTest);
        AssertScreenContains(Render(dialog), "EDITOR");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AVariableTypedNameThenValueIsSavedAndHighlighted()
    {
        var (dialog, user, _, _) = Create();
        await OpenAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "MY_TOOL_HOME");
        await EnterAsync(dialog);
        await TypeAsync(dialog, "/opt/my tool");
        await EnterAsync(dialog);
        await SettleAsync(dialog);

        Assert.AreEqual("/opt/my tool", user.Values["MY_TOOL_HOME"]);
        Assert.IsFalse(dialog.IsFormOpenForTest);
        Assert.AreEqual("MY_TOOL_HOME", dialog.SelectedNameForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABadNameKeepsTheFormOpenAndWritesNothing()
    {
        var (dialog, user, _, _) = Create();
        await OpenAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "A=B");
        await EnterAsync(dialog);
        await EnterAsync(dialog);
        await SettleAsync(dialog);

        Assert.IsTrue(dialog.IsFormOpenForTest);
        Assert.IsNotNull(dialog.FormErrorForTest);
        Assert.AreEqual(0, user.Values.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangingAVariableKeepsItsNameAndReplacesTheValue()
    {
        var (dialog, user, _, _) = Create();
        user.Values["EDITOR"] = "vim";
        await OpenAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('e'));
        for (var i = 0; i < 3; i++)
        {
            await BackspaceAsync(dialog);
        }

        await TypeAsync(dialog, "nano");
        await EnterAsync(dialog);
        await SettleAsync(dialog);

        CollectionAssert.AreEqual(new[] { "EDITOR" }, user.Values.Keys.ToArray());
        Assert.AreEqual("nano", user.Values["EDITOR"]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheNameCannotBeTypedOverWhileChangingAVariable()
    {
        var (dialog, user, _, _) = Create();
        user.Values["EDITOR"] = "vim";
        await OpenAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('e'));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        await TypeAsync(dialog, "X");
        await BackspaceAsync(dialog);
        await EnterAsync(dialog);
        await SettleAsync(dialog);

        CollectionAssert.AreEqual(new[] { "EDITOR" }, user.Values.Keys.ToArray());
        Assert.AreEqual("vim", user.Values["EDITOR"]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteAsksFirstAndOnlyYRemoves()
    {
        var (dialog, user, _, _) = Create();
        user.Values["A_VAR"] = "1";
        user.Values["B_VAR"] = "2";
        await OpenAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('d'));
        Assert.IsTrue(dialog.ConfirmingDeleteForTest);
        await dialog.HandleKeyEventAsync(Char('x'));
        await SettleAsync(dialog);
        Assert.AreEqual(2, user.Values.Count);

        await dialog.HandleKeyEventAsync(Char('d'));
        await dialog.HandleKeyEventAsync(Char('y'));
        await SettleAsync(dialog);

        CollectionAssert.AreEqual(new[] { "B_VAR" }, user.Values.Keys.ToArray());
        Assert.AreEqual("B_VAR", dialog.SelectedNameForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SCyclesThroughTheScopesTheSystemOffers()
    {
        var (dialog, user, machine, session) = Create();
        user.Values["U_VAR"] = "1";
        machine.Values["M_VAR"] = "2";
        session.Values["S_VAR"] = "3";
        await OpenAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('s'));
        await SettleAsync(dialog);
        Assert.AreEqual(EnvironmentVariablesDialog.EditScope.Machine, dialog.ScopeForTest);
        Assert.AreEqual("M_VAR", dialog.SelectedNameForTest);

        await dialog.HandleKeyEventAsync(Char('s'));
        await SettleAsync(dialog);
        Assert.AreEqual(EnvironmentVariablesDialog.EditScope.Session, dialog.ScopeForTest);
        Assert.AreEqual("S_VAR", dialog.SelectedNameForTest);

        await dialog.HandleKeyEventAsync(Char('s'));
        await SettleAsync(dialog);
        Assert.AreEqual(EnvironmentVariablesDialog.EditScope.User, dialog.ScopeForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SessionChangesGoToTheRunningProcessNotTheStores()
    {
        var (dialog, user, _, session) = Create();
        await OpenAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('s'));
        await dialog.HandleKeyEventAsync(Char('s'));
        await SettleAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "ONLY_HERE");
        await EnterAsync(dialog);
        await TypeAsync(dialog, "yes");
        await EnterAsync(dialog);
        await SettleAsync(dialog);

        Assert.AreEqual("yes", session.Values["ONLY_HERE"]);
        Assert.AreEqual(0, user.Values.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMachineChangeWithoutAdministratorRightsIsRefusedInWords()
    {
        var (dialog, _, machine, _) = Create(elevated: false);
        await OpenAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('s'));
        await SettleAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "NOPE");
        await EnterAsync(dialog);
        await TypeAsync(dialog, "1");
        await EnterAsync(dialog);
        await SettleAsync(dialog);

        Assert.AreEqual(0, machine.Values.Count);
        StringAssert.Contains(dialog.StatusForTest, "administrator");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFilterNarrowsByNameOrValueAndEscClearsItBeforeClosing()
    {
        var (dialog, user, _, _) = Create();
        user.Values["PATH_EXTRA"] = "/x";
        user.Values["EDITOR"] = "vim";
        await OpenAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('/'));
        await TypeAsync(dialog, "vim");
        Assert.IsTrue(dialog.IsFilteringForTest);
        Assert.AreEqual("EDITOR", dialog.SelectedNameForTest);

        await EnterAsync(dialog);
        Assert.IsFalse(dialog.IsFilteringForTest);
        var handled = await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape, (char)27));
        Assert.IsTrue(handled);
        AssertScreenContains(Render(dialog), "PATH_EXTRA");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscInTheFormDiscardsItWithoutClosingTheDialog()
    {
        var (dialog, user, _, _) = Create();
        await OpenAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(dialog, "TEMP");
        var handled = await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape, (char)27));

        Assert.IsTrue(handled);
        Assert.IsFalse(dialog.IsFormOpenForTest);
        Assert.AreEqual(0, user.Values.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheScopeNoteIsDrawnAndNoStaleTextRemainsAfterASwitch()
    {
        var (dialog, user, _, session) = Create();
        user.Values["LONG_USER_VARIABLE_NAME"] = "a very long value";
        session.Values["S"] = "1";
        await OpenAsync(dialog);
        AssertScreenContains(Render(dialog), "LONG_USER_VARIABLE_NAME");

        await dialog.HandleKeyEventAsync(Char('s'));
        await dialog.HandleKeyEventAsync(Char('s'));
        await SettleAsync(dialog);
        var screen = Render(dialog);

        AssertScreenContains(screen, "Seen by iTerminal");
        AssertScreenDoesNotContain(screen, "LONG_USER_VARIABLE_NAME");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASessionVariableWithAnUnportableNameCanStillBeChanged()
    {
        var (dialog, _, _, session) = Create();
        session.Values["A.B"] = "old";
        await OpenAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('s'));
        await dialog.HandleKeyEventAsync(Char('s'));
        await SettleAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('e'));
        for (var i = 0; i < 3; i++)
        {
            await BackspaceAsync(dialog);
        }

        await TypeAsync(dialog, "new");
        await EnterAsync(dialog);
        await SettleAsync(dialog);

        Assert.IsFalse(dialog.IsFormOpenForTest);
        Assert.AreEqual("new", session.Values["A.B"]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALongNoteIsWrappedAtSpacesInsteadOfCutOff()
    {
        const string Text = "Saved in ~/.zprofile; new login shells read it, running ones and non-login shells do not.";

        var lines = EnvironmentVariablesDialog.Wrap(Text, 50, 2);

        Assert.AreEqual(2, lines.Count);
        Assert.IsTrue(lines.All(l => l.Length <= 50));
        Assert.AreEqual(Text, string.Join(" ", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheKeyHintsNameTheKeysOfTheCurrentMode()
    {
        var (dialog, _, _, _) = Create();
        await OpenAsync(dialog);

        CollectionAssert.IsSubsetOf(new[] { "Insert/N", "Enter/F2/E", "Delete/D", "S", "R", "/" }, dialog.GetKeyHints().Select(h => h.Key).ToArray());

        await dialog.HandleKeyEventAsync(Char('n'));
        CollectionAssert.IsSubsetOf(new[] { "Enter/Tab", "Shift+Tab", "Esc" }, dialog.GetKeyHints().Select(h => h.Key).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedReadIsSaidOnTheDialog()
    {
        var service = new Mock<ISystemSettingsService>();
        service.Setup(s => s.SupportsEnvironment(EnvironmentScope.User)).Returns(true);
        service.Setup(s => s.GetEnvironmentAsync(It.IsAny<EnvironmentScope>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        var dialog = new EnvironmentVariablesDialog(service.Object, new MemorySession(), TestTheme());

        await OpenAsync(dialog);

        StringAssert.Contains(dialog.StatusForTest, "boom");
    }
}
