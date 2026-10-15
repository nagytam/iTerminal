using iConsole.Data;
using iConsole.Controls.Buttons;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// Selecting a folder ends the dialog: it navigates and closes, on every page and by either route —
/// Enter on the page's list, or the page's own Select button.
///
/// These tests drive the whole dialog rather than a page on its own, which is the point of them. The
/// per-page tests call <c>page.HandleKeyEventAsync</c> directly, and that is a path the app never
/// takes: <see cref="iConsole.Controls.Tabs.Tab.Controls"/> is overridden to return the *focused
/// page's children*, so a key routed through the dialog used to reach past the page into its list
/// box, and both halves of what a page does on Enter — run its select command, and close the dialog
/// through it — were skipped. Enter ticked a row's selection flag instead, and the selector sat there.
/// </summary>
[TestClass]
public class FolderSelectorSelectionTest
{
    private sealed record Fixture(
        FolderSelectorTestHarness Harness,
        FolderSelectorTestHarness.TestRoot Root,
        FolderSelectorDialog Dialog);

    private static async Task<Fixture> OpenAsync(int page, ISettingsService? settingsService = null)
    {
        var harness = new FolderSelectorTestHarness(settingsService);
        return await OpenAsync(page, harness);
    }

    private static async Task<Fixture> OpenAsync(int page, FolderSelectorTestHarness harness)
    {
        var root = new FolderSelectorTestHarness.TestRoot(harness.ThemeService);
        var dialog = new FolderSelectorDialog(
            harness.ContentModelService, harness.FolderHistoryService, harness.DriveInfoModel,
            harness.FolderIndexModel, harness.BeepService, harness.DateTimeProviderService,
            harness.SettingsService, harness.FrequentExecutionGuardService, harness.ThemeService)
        {
            ViewSize = new Size(100, 30)
        };
        await root.ShowDialogAsync(dialog);
        await FolderSelectorTestHarness.PressAsync(root, FolderSelectorTestHarness.Key(ConsoleKey.D0 + page, control: true));
        dialog.OnIdle();
        return new Fixture(harness, root, dialog);
    }

    private static async Task<bool> EnterAsync(Fixture fixture) =>
        await FolderSelectorTestHarness.PressAsync(fixture.Root, FolderSelectorTestHarness.Key(ConsoleKey.Enter));

    /// <summary>The dialog navigated exactly once and took itself off the screen doing it.</summary>
    private static void AssertWentAndClosed(Fixture fixture, string expectedUrl)
    {
        CollectionAssert.AreEqual(new[] { expectedUrl }, fixture.Harness.Navigations);
        Assert.IsFalse(fixture.Dialog.IsVisible, "The selector should close itself once a folder has been chosen.");
        Assert.IsFalse(fixture.Root.Controls.Contains(fixture.Dialog), "The selector should be off the screen, not merely invisible.");
    }

    [TestMethod]
    public async Task Enter_OnAFavourite_GoesThereAndCloses()
    {
        var settingsService = new SettingsService();
        settingsService.Set("Favourites", @"C:\one;C:\two");
        var fixture = await OpenAsync(0, settingsService);

        var handled = await EnterAsync(fixture);

        Assert.IsTrue(handled);
        AssertWentAndClosed(fixture, new Url(@"C:\one").FullUrl);
    }

    [TestMethod]
    public async Task Enter_OnADrive_GoesThereAndCloses()
    {
        var harness = new FolderSelectorTestHarness();
        harness.DriveInfoModel.Drives.Add(new Drive
        {
            DriveInfo = null,
            Name = Container.MEMORY_DRIVE,
            Url = Container.MEMORY_DRIVE
        });
        var fixture = await OpenAsync(1, harness);

        var handled = await EnterAsync(fixture);

        Assert.IsTrue(handled);
        AssertWentAndClosed(fixture, new Url(Container.MEMORY_DRIVE).FullUrl);
    }

    [TestMethod]
    public async Task Enter_OnAVisitedFolder_GoesBackThereAndCloses()
    {
        var harness = new FolderSelectorTestHarness();
        harness.FolderHistoryService.Add(new Url(Path.GetTempPath()));
        var fixture = await OpenAsync(2, harness);

        var handled = await EnterAsync(fixture);

        Assert.IsTrue(handled);
        AssertWentAndClosed(fixture, new Url(Path.GetTempPath()).FullUrl);
    }

    [TestMethod]
    public async Task Enter_OnAPlace_GoesThereAndCloses()
    {
        var fixture = await OpenAsync(3);
        var expected = ((FolderSelectorSpecialFoldersTabPage)fixture.Dialog.TabPages[3]).Places[0];

        var handled = await EnterAsync(fixture);

        Assert.IsTrue(handled);
        AssertWentAndClosed(fixture, new Url(expected).FullUrl);
    }

    [TestMethod]
    public async Task Enter_OnATreeFolder_GoesThereAndCloses()
    {
        var harness = new FolderSelectorTestHarness();
        harness.FolderIndexModel.Children.Add(new Folder(harness.FolderIndexModel, @"C:\projects"));
        var fixture = await OpenAsync(4, harness);
        harness.FolderIndexModel.IsFocused = false;
        foreach (var child in harness.FolderIndexModel.Children.AsSnapshot())
        {
            child.IsFocused = child.Name == @"C:\projects";
        }

        var handled = await EnterAsync(fixture);

        Assert.IsTrue(handled);
        AssertWentAndClosed(fixture, new Url(@"C:\projects").FullUrl);
    }

    [TestMethod]
    public async Task Enter_OnATypedHost_ConnectsAndCloses()
    {
        var fixture = await OpenAsync(5);
        var page = (FolderSelectorRemoteShellTabPage)fixture.Dialog.TabPages[5];
        page.TextBoxHost.TextModel.Lines[0] = "tamas@example.com";
        page.TextBoxHost.TextModel.TriggerPropertyChanged();
        page.TextBoxHost.ValidationModel?.Validate(DateTime.Now.AddMinutes(1));

        var handled = await EnterAsync(fixture);

        Assert.IsTrue(handled);
        AssertWentAndClosed(fixture, new Url(Container.SSH + "tamas@example.com:/").FullUrl);
    }

    [TestMethod]
    public async Task Enter_OnATypedPath_GoesThereAndCloses()
    {
        var fixture = await OpenAsync(6);
        var page = (FolderSelectorPathTabPage)fixture.Dialog.TabPages[6];
        page.TextBoxPath.TextModel.Lines[0] = Path.GetTempPath();
        page.TextBoxPath.TextModel.TriggerPropertyChanged();
        page.PathValidator.Validate(DateTime.Now.AddMinutes(1));

        var handled = await EnterAsync(fixture);

        Assert.IsTrue(handled);
        AssertWentAndClosed(fixture, new Url(Path.GetTempPath()).FullUrl);
    }

    [TestMethod]
    public async Task TheSelectButton_ClosesTheDialogToo()
    {
        // The other half of the promise: the same command reached by tabbing onto the page's own
        // Select button rather than by pressing Enter on its list.
        var fixture = await OpenAsync(3);
        var expected = ((FolderSelectorSpecialFoldersTabPage)fixture.Dialog.TabPages[3]).Places[0];
        await FolderSelectorTestHarness.PressAsync(fixture.Root, FolderSelectorTestHarness.Key(ConsoleKey.Tab));
        Assert.IsInstanceOfType<Button>(fixture.Dialog.GetFocusedControl());

        var handled = await EnterAsync(fixture);

        Assert.IsTrue(handled);
        AssertWentAndClosed(fixture, new Url(expected).FullUrl);
    }

    [TestMethod]
    public async Task Enter_WithNothingSelectable_LeavesTheDialogOpen()
    {
        // An empty favourites list has nothing to go to, and a selector that closed on Enter anyway
        // would look like it had navigated.
        var fixture = await OpenAsync(0);

        await EnterAsync(fixture);

        Assert.AreEqual(0, fixture.Harness.Navigations.Count);
        Assert.IsTrue(fixture.Dialog.IsVisible);
    }

    [TestMethod]
    public async Task Enter_OnAddToFavourites_AddsAndLeavesTheDialogOpen()
    {
        // A focused Button keeps Enter for itself, and "Add to favourites" is not a way of choosing
        // a folder — closing on it would make the button unusable.
        var fixture = await OpenAsync(0);
        var page = (FolderSelectorFavouritesTabPage)fixture.Dialog.TabPages[0];
        await FolderSelectorTestHarness.PressAsync(fixture.Root, FolderSelectorTestHarness.Key(ConsoleKey.Tab));
        Assert.IsInstanceOfType<Button>(fixture.Dialog.GetFocusedControl());

        await EnterAsync(fixture);

        Assert.AreEqual(1, page.Favourites.Count, "Enter on the button should have added the panel's folder.");
        Assert.AreEqual(0, fixture.Harness.Navigations.Count);
        Assert.IsTrue(fixture.Dialog.IsVisible, "Adding a favourite is not choosing a folder.");
    }
}
