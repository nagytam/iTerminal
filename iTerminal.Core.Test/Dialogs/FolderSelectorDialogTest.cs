using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// Ctrl+F1, the folder selector. What is worth pinning down is the promise the dialog makes: the
/// same three keys work on every page, the page strip is reachable without giving up the arrow keys
/// that drive the lists, every drive kind the app has can be reached from here, and the dialog says
/// so on its own border rather than only under F1.
/// </summary>
[TestClass]
public class FolderSelectorDialogTest
{
    private static FolderSelectorDialog CreateDialog(
        FolderSelectorTestHarness harness,
        HelpDialog? helpDialog = null)
    {
        return new FolderSelectorDialog(
            harness.ContentModelService,
            harness.FolderHistoryService,
            harness.DriveInfoModel,
            harness.FolderIndexModel,
            harness.BeepService,
            harness.DateTimeProviderService,
            harness.SettingsService,
            harness.FrequentExecutionGuardService,
            harness.ThemeService,
            helpDialog)
        {
            ViewSize = new Size(100, 30)
        };
    }

    [TestMethod]
    public void Constructor_ListsOnePageForEveryWayOfNamingAFolder()
    {
        var harness = new FolderSelectorTestHarness();

        var dialog = CreateDialog(harness);

        CollectionAssert.AreEqual(
            new[] { "0-Favourites", "1-Drives", "2-Recent", "3-Places", "4-Folder Tree", "5-SSH", "6-Path" },
            dialog.TabPages.Select(p => p.Name).ToArray());
    }

    [TestMethod]
    public void Constructor_GivesEveryPageItsOwnNumberedHotKey()
    {
        var harness = new FolderSelectorTestHarness();

        var dialog = CreateDialog(harness);

        var hotKeys = dialog.TabPages.Select(p => p.HotConsoleKey).ToList();
        CollectionAssert.AreEqual(
            new ConsoleKey?[]
            {
                ConsoleKey.D0, ConsoleKey.D1, ConsoleKey.D2, ConsoleKey.D3,
                ConsoleKey.D4, ConsoleKey.D5, ConsoleKey.D6
            },
            hotKeys);
        Assert.AreEqual(hotKeys.Count, hotKeys.Distinct().Count(), "Two pages sharing a hot key makes one unreachable.");
    }

    [TestMethod]
    public async Task Ctrl6_JumpsStraightToThePathPage()
    {
        var harness = new FolderSelectorTestHarness();
        var dialog = CreateDialog(harness);
        await dialog.OnInitializeAsync();

        var handled = await dialog.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.D6, control: true));

        Assert.IsTrue(handled);
        Assert.IsTrue(dialog.TabPages[6].IsFocused, "Ctrl+6 should focus the Path page.");
    }

    [TestMethod]
    public async Task CtrlDown_MovesToTheNextPage()
    {
        var harness = new FolderSelectorTestHarness();
        var dialog = CreateDialog(harness);
        await dialog.OnInitializeAsync();

        var handled = await dialog.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.DownArrow, control: true));

        Assert.IsTrue(handled);
        Assert.IsTrue(dialog.TabPages[1].IsFocused, "Ctrl+Down should move from Favourites to Drives.");
        Assert.IsFalse(dialog.TabPages[0].IsFocused);
    }

    [TestMethod]
    public async Task Escape_ClosesWithoutNavigating()
    {
        var harness = new FolderSelectorTestHarness();
        var dialog = CreateDialog(harness);
        await dialog.OnInitializeAsync();

        var handled = await dialog.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Escape));

        Assert.IsTrue(handled);
        Assert.IsFalse(dialog.IsVisible);
        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [TestMethod]
    public async Task Escape_ClosesFromEveryPage()
    {
        // Esc is claimed by Dialog ahead of the children rather than routed to the focused control
        // first, which is what makes it the one key that means the same thing on all seven pages —
        // including the two that are text boxes, where a focused control could plausibly want it.
        for (int page = 0; page < 7; page++)
        {
            var harness = new FolderSelectorTestHarness();
            var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
            var dialog = CreateDialog(harness);
            await root.ShowDialogAsync(dialog);
            await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D0 + page, control: true));

            var handled = await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Escape));

            Assert.IsTrue(handled, $"Esc was not handled on {dialog.TabPages[page].Name}.");
            Assert.IsFalse(dialog.IsVisible, $"Esc did not close the selector on {dialog.TabPages[page].Name}.");
            Assert.IsFalse(root.Controls.Contains(dialog), $"The selector was still on the root after Esc on {dialog.TabPages[page].Name}.");
            Assert.AreEqual(0, harness.Navigations.Count);
        }
    }

    [TestMethod]
    public async Task Escape_ClosesWhicheverControlOnThePageIsFocused()
    {
        // The Path page is the awkward one: a text box that swallows printable keys, an error
        // message, a label and a button. Tab through all of them and Esc has to keep working.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D6, control: true));

        for (int tabStop = 0; tabStop < 4; tabStop++)
        {
            await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Tab));
        }

        var handled = await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Escape));

        Assert.IsTrue(handled);
        Assert.IsFalse(dialog.IsVisible);
        Assert.AreEqual(0, harness.Navigations.Count);
    }

    /// <summary>
    /// Opens the Path page with its suggestion list up, and hands back the completion so a test can
    /// ask whether the list is still there. Typing is what shows the list, so a completion is planted
    /// first and then a character typed — the same two things that happen in the app.
    /// </summary>
    private static async Task<AutoCompletion> OpenPathPageWithSuggestionsAsync(
        FolderSelectorTestHarness.TestRoot root,
        FolderSelectorDialog dialog)
    {
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D6, control: true));
        var pathPage = (FolderSelectorPathTabPage)dialog.TabPages[6];
        var autoCompletion = pathPage.TextBoxPath.AutoCompletion!;
        // The planted completion has to contain the character typed after it, wherever the temp folder
        // is: matching is a substring test, and the temp path only has a 'C' on Windows.
        autoCompletion.CompletionModel.InsertCompletion(Path.Combine(Path.GetTempPath(), "Completion"));
        await FolderSelectorTestHarness.PressAsync(root, new KeyEvent('C', ConsoleKey.C, false, false, false));
        await WaitForAsync(() => autoCompletion.IsPopupOpen);
        Assert.IsTrue(autoCompletion.IsPopupOpen, "The suggestion list should be up before the key under test.");
        return autoCompletion;
    }

    [TestMethod]
    public async Task Escape_WithTheCompletionListOpen_TakesTheListFirstAndTheSelectorNext()
    {
        // One press per layer. Esc while a suggestion list is up means "never mind that list" — closing
        // the whole selector on it throws away the path being typed, and it left the list with no key
        // that could dismiss it at all, since Dialog claims Esc ahead of every child.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        var autoCompletion = await OpenPathPageWithSuggestionsAsync(root, dialog);

        var handled = await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Escape));

        Assert.IsTrue(handled);
        Assert.IsFalse(autoCompletion.IsPopupOpen, "The first Esc should have dismissed the suggestion list.");
        Assert.IsTrue(dialog.IsVisible, "The first Esc should have left the selector open.");

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Escape));

        Assert.IsFalse(dialog.IsVisible, "The second Esc should have closed the selector.");
        Assert.AreEqual(0, root.Controls.Count, "Esc must leave nothing of the selector attached to the root.");
        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [TestMethod]
    public async Task Escape_ThenTyping_BringsTheCompletionListBack()
    {
        // Dismissing the list has to cost nothing, or Esc becomes a key nobody dares press: the text
        // is left alone, so the next character shows the suggestions again.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D6, control: true));
        var page = (FolderSelectorPathTabPage)dialog.TabPages[6];
        var autoCompletion = page.TextBoxPath.AutoCompletion!;
        // Two completions sharing a prefix this test controls, so the second keystroke is still a
        // prefix of them both — unlike a real path, which starts with whatever drive the box is on.
        autoCompletion.CompletionModel.InsertCompletion("zz-one");
        autoCompletion.CompletionModel.InsertCompletion("zz-two");
        var typeZ = new KeyEvent('z', ConsoleKey.Z, false, false, false);
        await FolderSelectorTestHarness.PressAsync(root, typeZ);
        await WaitForAsync(() => autoCompletion.IsPopupOpen);
        Assert.IsTrue(autoCompletion.IsPopupOpen);

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Escape));
        Assert.IsFalse(autoCompletion.IsPopupOpen);
        Assert.AreEqual("z", page.Path, "Esc must not edit the text.");

        await FolderSelectorTestHarness.PressAsync(root, typeZ);
        await WaitForAsync(() => autoCompletion.IsPopupOpen);

        Assert.IsTrue(autoCompletion.IsPopupOpen, "The next keystroke should bring the suggestions back.");
        Assert.IsTrue(dialog.IsVisible);
    }

    [TestMethod]
    public async Task ClosingTheSelector_WithTheCompletionListOpen_TakesTheListWithIt()
    {
        // The suggestion list is a Popup, and a Popup attaches itself to the root rather than to
        // the text box that owns it. Closing the dialog therefore has to take it down as well, or
        // the selector leaves a floating list behind over the panels.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        var autoCompletion = await OpenPathPageWithSuggestionsAsync(root, dialog);

        dialog.Close();

        Assert.IsFalse(autoCompletion.IsPopupOpen);
        Assert.AreEqual(0, root.Controls.Count);
    }

    [TestMethod]
    public async Task TheCompletionList_OpensOnTheRowDirectlyBelowTheBoxItBelongsTo()
    {
        // A Popup is placed below its owner, which means the owner's height decides where the
        // suggestions land. The Path box is one row — that is all a TextBox draws — and while it was
        // laid out as tall as the whole page its suggestions appeared at the bottom of the screen, a
        // dozen rows away from the text they were completing.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        var autoCompletion = await OpenPathPageWithSuggestionsAsync(root, dialog);
        var textBox = ((FolderSelectorPathTabPage)dialog.TabPages[6]).TextBoxPath;

        var popup = autoCompletion.ListPopup;

        Assert.AreEqual(1, (int)textBox.ViewSize.Height, "A TextBox draws one row; anything else misplaces its list.");
        Assert.AreEqual(
            (int)textBox.AbsolutePosition.Y + 1,
            (int)popup.AbsolutePosition.Y,
            "The suggestions belong on the row under the box.");
        Assert.AreEqual(
            (int)textBox.AbsolutePosition.X,
            (int)popup.AbsolutePosition.X,
            "The suggestions belong in the box's own column.");
        Assert.AreEqual((int)textBox.ViewSize.Width, (int)popup.ViewSize.Width);
    }

    [TestMethod]
    public async Task SwitchingPage_TakesTheCompletionListWithIt()
    {
        // The list is attached to the root, so leaving the Path page does not hide it: it would be
        // left floating over whichever page arrives in its place.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        var autoCompletion = await OpenPathPageWithSuggestionsAsync(root, dialog);

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D0, control: true));

        Assert.IsTrue(dialog.TabPages[0].IsFocused);
        Assert.IsFalse(autoCompletion.IsPopupOpen, "The suggestions belonged to the page that was left.");
    }

    [TestMethod]
    public async Task Enter_WithTheCompletionListOpen_StillGoesToTheTypedFolderAndCloses()
    {
        // Enter means the same thing whether or not suggestions are showing: go, and close.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        await OpenPathPageWithSuggestionsAsync(root, dialog);
        var page = (FolderSelectorPathTabPage)dialog.TabPages[6];
        page.TextBoxPath.TextModel.Lines[0] = Path.GetTempPath();
        page.TextBoxPath.TextModel.TriggerPropertyChanged();
        page.PathValidator.Validate(DateTime.Now.AddMinutes(1));

        var handled = await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { new Url(Path.GetTempPath()).FullUrl }, harness.Navigations);
        Assert.IsFalse(dialog.IsVisible);
        Assert.AreEqual(0, root.Controls.Count, "Neither the selector nor its suggestion list may be left behind.");
    }

    [TestMethod]
    public async Task SelectingAPage_PutsTheCursorOnThatPagesFirstControl()
    {
        // Whichever way a page is reached, its first control holds the cursor — otherwise the arrows
        // do nothing until the user has found their way in with Tab. A page cannot hold focus itself,
        // so this is the first *focusable* control, which is why every page lists its list or its text
        // box before its buttons.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);

        for (int page = 0; page < dialog.TabPages.Count; page++)
        {
            await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D0 + page, control: true));

            var tabPage = dialog.TabPages[page];
            Assert.IsTrue(tabPage.IsFocused, $"Ctrl+{page} did not show {tabPage.Name}.");
            Assert.AreSame(
                tabPage.Controls[0],
                tabPage.GetFocusedControl(),
                $"{tabPage.Name} did not put the cursor on its first control.");
        }
    }

    [TestMethod]
    public async Task ThePageHotKey_OnThePageAlreadyShowing_BringsTheCursorBackToItsFirstControl()
    {
        // The keyboard's counterpart to clicking the name of the page already up. It matters most on
        // the Path page, which keeps Tab for its own completion — reaching a button there is easy and
        // there was no key that brought the cursor back.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        var page = dialog.TabPages[0];
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Tab));
        Assert.IsInstanceOfType<Button>(page.GetFocusedControl(), "The cursor has to be off the list first.");

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D0, control: true));

        Assert.AreSame(page.Controls[0], page.GetFocusedControl());
    }

    [TestMethod]
    public async Task Escape_WithHelpOpen_ClosesTheHelpFirstAndTheSelectorNext()
    {
        // F1 shows the help as a *sibling* of the selector on the root, not as its child. The keys
        // belong to whichever of the two is on top, so the first Esc has to close the help and
        // leave the selector standing — read the other way round the selector closed underneath a
        // help overlay that then had nothing left to return to.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var helpDialog = new HelpDialog(harness.ThemeService);
        var dialog = CreateDialog(harness, helpDialog);
        await root.ShowDialogAsync(dialog);

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.F1));
        Assert.AreSame(helpDialog, root.GetModalChildControl(), "The help is on top, so the keys are its.");

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Escape));

        Assert.IsFalse(helpDialog.IsVisible, "The first Esc should have closed the help.");
        Assert.IsTrue(dialog.IsVisible, "The first Esc should have left the selector open.");

        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.Escape));

        Assert.IsFalse(dialog.IsVisible, "The second Esc should have closed the selector.");
        Assert.AreEqual(0, root.Controls.Count);
        Assert.AreEqual(0, harness.Navigations.Count);
    }

    /// <summary>
    /// The screen cell holding the name of page <paramref name="page"/> in the left strip. The strip
    /// is painted by the `Tab` and owns no controls, so there is nothing to ask for its position:
    /// it is derived from where the pages start, which is exactly the strip's width to the right of
    /// it, and from the strip's own row arithmetic — a top border, then a name and a rule per page.
    /// </summary>
    private static (int X, int Y) StripCellOf(FolderSelectorDialog dialog, int page)
    {
        var stripWidth = dialog.TabPages.Max(p => p.Name.Length) + 6;
        var firstPage = dialog.TabPages[0].AbsolutePosition;
        return ((int)firstPage.X - stripWidth + 2, (int)firstPage.Y + 1 + (2 * page));
    }

    [TestMethod]
    public async Task ClickingAPageNameInTheLeftStrip_ShowsThatPageAndFocusesItsContent()
    {
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        var (x, y) = StripCellOf(dialog, 3);

        var handled = await FolderSelectorTestHarness.ClickAsync(root, x, y);

        Assert.IsTrue(handled, "A click on a page name must be picked up by the strip.");
        Assert.IsTrue(dialog.TabPages[3].IsFocused, "Clicking '3-Places' should show that page.");
        Assert.IsInstanceOfType<ListBox<string>>(
            dialog.TabPages[3].GetFocusedControl(),
            "The cursor should land in the panel on the right, not stay on the strip.");
    }

    [TestMethod]
    public async Task ClickingThePageAlreadyShowing_PutsTheCursorBackInItsPanel()
    {
        // The way back into the content from wherever the focus had got to — the Path page keeps
        // Tab for its own completion, so the strip is the obvious thing to reach for.
        var harness = new FolderSelectorTestHarness();
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = CreateDialog(harness);
        await root.ShowDialogAsync(dialog);
        root.ResetFocus();
        var (x, y) = StripCellOf(dialog, 0);

        var handled = await FolderSelectorTestHarness.ClickAsync(root, x, y);

        Assert.IsTrue(handled);
        Assert.IsInstanceOfType<ListBox<Url>>(dialog.TabPages[0].GetFocusedControl());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    [TestMethod]
    public void Constructor_WithHelpDialog_BindsF1()
    {
        var harness = new FolderSelectorTestHarness();
        var helpDialog = new HelpDialog(harness.ThemeService);

        var dialog = CreateDialog(harness, helpDialog);

        var helpCommand = dialog.Commands.OfType<HelpDialogCommand>().SingleOrDefault();
        Assert.IsNotNull(helpCommand, "The selector should advertise its own help.");
        Assert.AreEqual(ConsoleKey.F1, helpCommand.Key?.ConsoleKey);
        Assert.IsFalse(helpCommand.Key!.Control || helpCommand.Key.Alt || helpCommand.Key.Shift);
    }

    [TestMethod]
    public void Constructor_WithoutHelpDialog_BindsNoF1()
    {
        var harness = new FolderSelectorTestHarness();

        var dialog = CreateDialog(harness);

        Assert.IsFalse(dialog.Commands.Any(c => c is HelpDialogCommand));
    }

    [TestMethod]
    public void GetFooter_NamesTheKeysThatWorkOnEveryPage()
    {
        var footer = FolderSelectorDialog.GetFooter();

        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(footer, "Enter");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(footer, "Ctrl+Up/Down");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(footer, "F1");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(footer, "Esc");
    }

    [TestMethod]
    public void GetFooter_OnANarrowBorder_LeavesHintsOutWholeAndKeepsGoHelpAndCancel()
    {
        var footer = FolderSelectorDialog.GetFooter(60);

        Assert.IsTrue(footer.Length <= 60, footer);
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(footer, "Enter go");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(footer, "F1 help");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(footer, "Esc cancel");
        Assert.IsFalse(footer.Contains("Ctrl+Up/Down", StringComparison.Ordinal), footer);
    }

    [TestMethod]
    public async Task Draw_PutsTheKeyHintsOnTheBottomBorder()
    {
        var harness = new FolderSelectorTestHarness();
        var buffer = new MockBuffer(new Size(100, 30));
        var dialog = CreateDialog(harness);
        await dialog.ShowAsync();

        dialog.Draw(buffer);

        var lastLine = buffer.ContentAsString.Split(Environment.NewLine)[29];
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(lastLine, "Enter go", "The footer must survive being drawn over the border.");
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(lastLine, "Esc cancel");
    }

    [TestMethod]
    public async Task Draw_InANarrowTerminal_DoesNotThrow()
    {
        // AlignCenter drops a footer longer than the line whole; the dialog shrinks it first, and a
        // width that leaves no room at all must still draw a border rather than throw.
        var harness = new FolderSelectorTestHarness();
        var buffer = new MockBuffer(new Size(20, 12));
        var dialog = CreateDialog(harness);
        dialog.ViewSize = new Size(20, 12);
        await dialog.ShowAsync();

        dialog.Draw(buffer);

        Assert.IsNotNull(buffer.ContentAsString);
    }

    [TestMethod]
    public void GetKeyHints_DocumentTheThreeKeysAndThePageSwitch()
    {
        var harness = new FolderSelectorTestHarness();
        var dialog = CreateDialog(harness);

        var hints = dialog.GetKeyHints().ToList();

        Assert.IsTrue(hints.Any(h => h.Key == "Enter"));
        Assert.IsTrue(hints.Any(h => h.Key == "Esc"));
        Assert.IsTrue(hints.Any(h => h.Key.Contains("Ctrl+Up")));
        Assert.IsTrue(hints.All(h => !string.IsNullOrWhiteSpace(h.Description)));
    }

    [TestMethod]
    public async Task OnInitialize_FocusesSomethingOnTheFirstPage()
    {
        // Nothing focused means the arrow keys reach the tab strip and the list cannot be moved at
        // all — which is what the selector looked like before the pages put their list first.
        var harness = new FolderSelectorTestHarness();
        var dialog = CreateDialog(harness);

        await dialog.ShowAsync();

        var focused = dialog.GetFocusedControl();
        Assert.IsNotNull(focused);
        Assert.AreNotSame(dialog, focused, "Focus should have landed inside a page, not on the dialog itself.");
        Assert.IsTrue(focused!.CanFocus);
    }

    [TestMethod]
    public void EveryPage_DocumentsTheSameThreeKeysInItsOwnWords()
    {
        var harness = new FolderSelectorTestHarness();
        var dialog = CreateDialog(harness);

        var enterDescriptions = new List<string>();
        foreach (var tabPage in dialog.TabPages)
        {
            var hints = tabPage.GetKeyHints().ToList();
            var enterHint = hints.SingleOrDefault(h => h.Key == "Enter");
            Assert.IsNotNull(enterHint.Description, $"{tabPage.Name} does not say what Enter does.");
            Assert.IsTrue(hints.Any(h => h.Key == "Esc"), $"{tabPage.Name} does not say how to cancel.");
            Assert.IsTrue(hints.Any(h => h.Key == "F1"), $"{tabPage.Name} does not point at help.");
            enterDescriptions.Add(enterHint.Description);
        }

        Assert.AreEqual(
            enterDescriptions.Count,
            enterDescriptions.Distinct().Count(),
            "Every page's Enter does something different; the help should say which.");
    }
}
