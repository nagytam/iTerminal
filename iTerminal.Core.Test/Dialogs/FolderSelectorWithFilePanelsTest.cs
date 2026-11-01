using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Screens;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// The folder selector driven through the real screen tree: two real <see cref="FilePanel"/>s over a
/// real <see cref="ContentModelService"/>, the dialog opened by the real
/// <see cref="FolderSelectorDialogCommand"/>, and every key routed the way
/// <c>KeyboardEventConsumerJob</c> routes it — to the topmost modal, not to whatever control the test
/// happens to hold.
///
/// <para><see cref="FolderSelectorDialogTest"/> covers the same four promises against a stand-in root,
/// which is faster and enough for most of them. This is the harness that answers the one question a
/// stand-in cannot: whether Enter actually moves <b>the pane the user was in</b>. There the navigation
/// ends at a mocked <c>SetUrlAsync</c> and the assertion is "the service was asked"; here it ends on a
/// real content model and the assertion is "the panel is showing the folder" — and that the other
/// panel is not.</para>
/// </summary>
[TestClass]
public class FolderSelectorWithFilePanelsTest
{
    private sealed class Harness : IAsyncDisposable
    {
        public required Screen Screen { get; init; }
        public required FilePanel LeftPanel { get; init; }
        public required FilePanel RightPanel { get; init; }
        public required ContentModelService ContentModelService { get; init; }
        public required FolderSelectorDialogCommand OpenCommand { get; init; }
        public required string TempRoot { get; init; }
        public required string ChildFolder { get; init; }

        /// <summary>The dialog currently on the screen, or null once it has closed.</summary>
        public FolderSelectorDialog? Dialog =>
            Screen.Controls.AsSnapshot().OfType<FolderSelectorDialog>().LastOrDefault();

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(TempRoot, recursive: true); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<Harness> BuildAsync()
    {
        var themeService = new ThemeService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "FolderSelectorPanelTest_" + Guid.NewGuid().ToString("N")[..8]);
        var childFolder = Path.Combine(tempRoot, "target-folder");
        Directory.CreateDirectory(childFolder);
        Directory.CreateDirectory(Path.Combine(tempRoot, "other-folder"));

        var taskManager = new TaskManager();
        var memoryCacheService = new MemoryCacheService();
        var settingsService = new SettingsService();
        var folderHistory = new FolderHistoryService(settingsService);
        var frequentGuard = new FrequentExecutionGuardService(new DateTimeProviderService());
        var fileSystemService = new FileSystemService();
        var logger = Serilog.Core.Logger.None;

        settingsService.Set(ContentModelService.Left, "file://" + tempRoot);
        settingsService.Set(ContentModelService.Right, "file://" + tempRoot);

        var contentModelService = new ContentModelService(
            taskManager, settingsService, fileSystemService,
            Mock.Of<IMemoryDriveService>(), Mock.Of<ISshFileSystemService>(),
            Mock.Of<ISmbFileSystemService>(), Mock.Of<IIPhoneFileSystemService>(),
            Mock.Of<INetworkProfileService>(), Mock.Of<IPasswordManager>(),
            Mock.Of<IArchiveFileSystemService>(),
            folderHistory, frequentGuard, memoryCacheService, logger,
            dbContextFactory: null,
            stopwatchFactory: null);

        var terminalModel = new TerminalModel(taskManager, contentModelService);
        var factory = new FilePanelFactory(
            taskManager, contentModelService, terminalModel, memoryCacheService, settingsService, logger, themeService);

        var leftPanel = factory.Create(ContentModelService.Left);
        leftPanel.ViewSize = new Size(60, 24);
        await leftPanel.OnInitializeAsync();

        var rightPanel = factory.Create(ContentModelService.Right);
        rightPanel.Position = new Position(60, 0);
        rightPanel.ViewSize = new Size(60, 24);
        await rightPanel.OnInitializeAsync();

        var screen = new Screen(NullConsoleDriver.Instance, themeService) { ViewSize = new Size(120, 40) };
        screen.AddControl(leftPanel);
        screen.AddControl(rightPanel);

        // The app hangs this off the status bar's HotKeys; what matters here is that it is reached
        // only after the focused panel has declined the chord, which is true either way.
        var openCommand = new FolderSelectorDialogCommand(
            contentModelService, folderHistory, new DriveInfoModel(), new FolderIndexModel(),
            Mock.Of<IBeepService>(), new DateTimeProviderService(), settingsService, frequentGuard, themeService);
        screen.AddCommand(openCommand);

        await screen.OnInitializeAsync();
        leftPanel.SetFocus();
        Assert.IsTrue(leftPanel.IsFocused, "Precondition: a file panel holds focus, as it does in the app.");

        return new Harness
        {
            Screen = screen,
            LeftPanel = leftPanel,
            RightPanel = rightPanel,
            ContentModelService = contentModelService,
            OpenCommand = openCommand,
            TempRoot = tempRoot,
            ChildFolder = childFolder,
        };
    }

    /// <summary>
    /// Presses a key the way the app does: <c>Desktop.GetModalChildControl()</c> picks the topmost
    /// modal out of the screen's children — looking inside a control before taking it, in reverse
    /// order — and <c>KeyboardEventConsumerJob</c> hands the key to that and nothing else.
    /// </summary>
    private static async Task<bool> PressAsync(Screen screen, KeyEvent keyEvent)
    {
        IControl target = screen;
        foreach (var control in screen.Controls.AsSnapshot().Reverse())
        {
            var modalControl = control.GetModalChildControl();
            if (modalControl is not null)
            {
                target = modalControl;
                break;
            }
            if (control.IsModal)
            {
                target = control;
                break;
            }
        }
        return await target.HandleKeyEventAsync(keyEvent);
    }

    private static KeyEvent Key(ConsoleKey consoleKey, bool control = false) =>
        new('\0', consoleKey, shift: false, alt: false, control: control);

    /// <summary>Ctrl+F1, the chord the app binds — and it has to survive the focused panel.</summary>
    private static async Task<FolderSelectorDialog> OpenSelectorAsync(Harness harness)
    {
        var handled = await PressAsync(harness.Screen, Key(ConsoleKey.F1, control: true));
        Assert.IsTrue(handled, "Ctrl+F1 should have been handled by the screen's control tree.");
        var dialog = harness.Dialog;
        Assert.IsNotNull(dialog, "Ctrl+F1 should have put the folder selector on the screen.");
        return dialog!;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnATypedFolder_MovesTheFocusedPaneAndClosesTheDialog()
    {
        await using var harness = await BuildAsync();
        // Captured up front, and asserted on afterwards by identity. Re-asking for "the focused model"
        // once the dialog has closed is a different question — whatever holds the cursor at the end —
        // and the promise being tested is about the pane the user was in, which is this object.
        var focusedModel = harness.ContentModelService.GetFocusedModel();
        Assert.IsNotNull(focusedModel, "Precondition: a pane is focused.");
        var otherModel = harness.ContentModelService.GetModel(ContentModelService.Right)!;
        var otherModelUrl = otherModel.Url.FullUrl;
        var dialog = await OpenSelectorAsync(harness);

        await PressAsync(harness.Screen, Key(ConsoleKey.D6, control: true));
        var page = (FolderSelectorPathTabPage)dialog.TabPages[6];
        page.TextBoxPath.TextModel.Lines[0] = harness.ChildFolder;
        page.TextBoxPath.TextModel.TriggerPropertyChanged();
        page.PathValidator.Validate(DateTime.Now.AddMinutes(1));

        var handled = await PressAsync(harness.Screen, Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        Assert.AreEqual(
            new Url(harness.ChildFolder).FullUrl,
            focusedModel!.Url.FullUrl,
            "The pane the user was in should be showing the chosen folder.");
        Assert.AreEqual(
            otherModelUrl,
            otherModel.Url.FullUrl,
            "Only the pane the user was in should have moved.");
        Assert.IsNull(harness.Dialog, "Choosing a folder ends the dialog.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SelectingAPage_PutsTheCursorOnItsFirstControl()
    {
        await using var harness = await BuildAsync();
        var dialog = await OpenSelectorAsync(harness);

        for (int page = 0; page < dialog.TabPages.Count; page++)
        {
            await PressAsync(harness.Screen, Key(ConsoleKey.D0 + page, control: true));

            var tabPage = dialog.TabPages[page];
            Assert.IsTrue(tabPage.IsFocused, $"Ctrl+{page} did not show {tabPage.Name}.");
            Assert.AreSame(
                tabPage.Controls[0],
                tabPage.GetFocusedControl(),
                $"{tabPage.Name} did not put the cursor on its first control.");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheCompletionList_OpensDirectlyBelowTheBox()
    {
        await using var harness = await BuildAsync();
        var dialog = await OpenSelectorAsync(harness);
        await PressAsync(harness.Screen, Key(ConsoleKey.D6, control: true));
        var page = (FolderSelectorPathTabPage)dialog.TabPages[6];
        var autoCompletion = page.TextBoxPath.AutoCompletion!;
        autoCompletion.CompletionModel.InsertCompletion("zz-one");
        autoCompletion.CompletionModel.InsertCompletion("zz-two");

        await PressAsync(harness.Screen, new KeyEvent('z', ConsoleKey.Z, false, false, false));
        await WaitForAsync(() => autoCompletion.IsPopupOpen);

        Assert.IsTrue(autoCompletion.IsPopupOpen, "Typing should have shown the suggestions.");
        var box = page.TextBoxPath;
        var popup = autoCompletion.ListPopup;
        Assert.AreEqual((int)box.AbsolutePosition.Y + 1, (int)popup.AbsolutePosition.Y,
            "The suggestions belong on the row under the box, not at the bottom of the screen.");
        Assert.AreEqual((int)box.AbsolutePosition.X, (int)popup.AbsolutePosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_TakesTheCompletionListFirstAndTheDialogNext()
    {
        await using var harness = await BuildAsync();
        var dialog = await OpenSelectorAsync(harness);
        await PressAsync(harness.Screen, Key(ConsoleKey.D6, control: true));
        var page = (FolderSelectorPathTabPage)dialog.TabPages[6];
        var autoCompletion = page.TextBoxPath.AutoCompletion!;
        autoCompletion.CompletionModel.InsertCompletion("zz-one");
        await PressAsync(harness.Screen, new KeyEvent('z', ConsoleKey.Z, false, false, false));
        await WaitForAsync(() => autoCompletion.IsPopupOpen);
        Assert.IsTrue(autoCompletion.IsPopupOpen);

        var handled = await PressAsync(harness.Screen, Key(ConsoleKey.Escape));

        Assert.IsTrue(handled);
        Assert.IsFalse(autoCompletion.IsPopupOpen, "The first Esc takes the suggestion list.");
        Assert.IsNotNull(harness.Dialog, "The first Esc must leave the selector standing.");
        Assert.AreEqual("z", page.Path, "Esc must not edit the text being typed.");

        await PressAsync(harness.Screen, Key(ConsoleKey.Escape));

        Assert.IsNull(harness.Dialog, "The second Esc closes the selector.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_LeavesTheFocusedPaneWhereItWas()
    {
        // Cancelling has to be free: the panes are what the dialog was opened over, and a selector
        // that moved one on the way out would be worse than useless.
        await using var harness = await BuildAsync();
        var pane = harness.ContentModelService.GetFocusedModel()!;
        var before = pane.Url.FullUrl;
        await OpenSelectorAsync(harness);

        await PressAsync(harness.Screen, Key(ConsoleKey.Escape));

        Assert.IsNull(harness.Dialog);
        Assert.AreEqual(before, pane.Url.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClosingTheSelector_LeavesNothingFloatingOverThePanels()
    {
        // The suggestion list is attached to the *screen*, not to the dialog, so it is the one thing
        // that can outlive the selector and sit over the panels for the rest of the run.
        await using var harness = await BuildAsync();
        var dialog = await OpenSelectorAsync(harness);
        await PressAsync(harness.Screen, Key(ConsoleKey.D6, control: true));
        var page = (FolderSelectorPathTabPage)dialog.TabPages[6];
        var autoCompletion = page.TextBoxPath.AutoCompletion!;
        autoCompletion.CompletionModel.InsertCompletion("zz-one");
        await PressAsync(harness.Screen, new KeyEvent('z', ConsoleKey.Z, false, false, false));
        await WaitForAsync(() => autoCompletion.IsPopupOpen);

        await PressAsync(harness.Screen, Key(ConsoleKey.Escape));
        await PressAsync(harness.Screen, Key(ConsoleKey.Escape));

        Assert.IsNull(harness.Dialog);
        Assert.IsFalse(autoCompletion.IsPopupOpen);
        CollectionAssert.AreEquivalent(
            new IControl[] { harness.LeftPanel, harness.RightPanel },
            harness.Screen.Controls.AsSnapshot().ToArray(),
            "Only the two panels should be left on the screen.");
    }
}
