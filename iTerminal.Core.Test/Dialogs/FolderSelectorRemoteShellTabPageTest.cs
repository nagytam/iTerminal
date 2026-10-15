using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class FolderSelectorRemoteShellTabPageTest
{
    private static readonly Size PageSize = new(60, 20);

    private static async Task<(FolderSelectorRemoteShellTabPage page, FolderSelectorTestHarness harness)> CreatePageAsync(
        string? host = null)
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorRemoteShellTabPage(
            harness.ContentModelService,
            harness.BeepService,
            harness.DateTimeProviderService,
            harness.SettingsService,
            harness.FrequentExecutionGuardService,
            harness.ThemeService)
        {
            ViewSize = PageSize
        };
        await page.OnInitializeAsync();
        if (host is not null)
        {
            SetHost(page, host);
        }
        return (page, harness);
    }

    /// <summary>
    /// Types into the box the way the key handler does, then settles the validator itself. The text
    /// change also kicks it off on a background task, but waiting for that is a race the test loses
    /// on a busy machine; the verdict is a pure function of the text. The stamp is pushed a minute
    /// ahead so the background pass cannot land afterwards and look like a later result.
    /// </summary>
    private static void SetHost(FolderSelectorRemoteShellTabPage page, string host)
    {
        page.TextBoxHost.TextModel.Lines[0] = host;
        page.TextBoxHost.TextModel.TriggerPropertyChanged();
        page.TextBoxHost.ValidationModel?.Validate(DateTime.Now.AddMinutes(1));
    }

    [TestMethod]
    public async Task Enter_WithAHost_ConnectsOverSsh()
    {
        var (page, harness) = await CreatePageAsync("tamas@example.com");
        page.SetFocus();

        var handled = await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { "ssh://tamas@example.com:/" }, harness.Navigations);
    }

    [TestMethod]
    public async Task Enter_WithAnEmptyBox_ConnectsNowhere()
    {
        var (page, harness) = await CreatePageAsync();
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [TestMethod]
    public async Task Host_ReportsWhatWasTyped()
    {
        var (page, _) = await CreatePageAsync("tamas@example.com");

        Assert.AreEqual("tamas@example.com", page.Host);
    }

    [TestMethod]
    public async Task Enter_RemembersTheHostForNextTime()
    {
        var (page, _) = await CreatePageAsync("tamas@example.com");
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        var completions = page.TextBoxHost.AutoCompletion?.CompletionModel.Completions.ToList();
        Assert.IsNotNull(completions);
        Assert.IsTrue(completions!.Any(c => c.Contains("example.com")), "The host should be offered next time.");
    }

    [TestMethod]
    public async Task GetKeyHints_NameTabCompletion()
    {
        var (page, _) = await CreatePageAsync();

        var hints = page.GetKeyHints().ToList();

        Assert.IsTrue(hints.Any(h => h.Key == "Tab"), "Tab completion is invisible unless it is written down.");
        Assert.IsTrue(hints.Any(h => h.Key == "Enter"));
    }

    [TestMethod]
    public async Task OnInitializeLayout_MakesTheHostBoxOneRowHigh()
    {
        // Not cosmetic: the completion list is a Popup placed directly below its owner, so a box
        // claiming more rows than the single line a TextBox draws strands the suggestions that many
        // rows further down the screen.
        var (page, _) = await CreatePageAsync();

        page.OnInitializeLayout();

        Assert.AreEqual(1, (int)page.TextBoxHost.ViewSize.Height);
    }

    [TestMethod]
    public void OnInitializeLayout_InANarrowPage_DoesNotProduceNegativeGeometry()
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorRemoteShellTabPage(
            harness.ContentModelService,
            harness.BeepService,
            harness.DateTimeProviderService,
            harness.SettingsService,
            harness.FrequentExecutionGuardService,
            harness.ThemeService)
        {
            ViewSize = new Size(1, 1)
        };

        page.OnInitializeLayout();

        foreach (var control in page.Controls.AsSnapshot())
        {
            Assert.IsTrue(control.ViewSize.Width >= 0 && control.ViewSize.Height >= 0);
        }
    }
}
