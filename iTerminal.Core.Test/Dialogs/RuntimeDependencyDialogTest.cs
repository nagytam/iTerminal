using iConsole.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.RuntimeDependencies;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class RuntimeDependencyDialogTest
{
    private static Mock<IRuntimeDependencyService> NewService(RuntimeDependencyState state, bool busy = false)
    {
        var service = new Mock<IRuntimeDependencyService>();
        RuntimeDependencyStatus status = state switch
        {
            RuntimeDependencyState.Missing => new RuntimeDependencyStatus("playwright", "Playwright", "Chromium for the quote indicator", null, "1.63.0", state),
            RuntimeDependencyState.UpdateAvailable => new RuntimeDependencyStatus("playwright", "Playwright", "Chromium for the quote indicator", "1.63.0", "1.63.2", state),
            _ => new RuntimeDependencyStatus("playwright", "Playwright", "Chromium for the quote indicator", "1.63.0", null, state),
        };
        service.SetupGet(s => s.Statuses).Returns([status]);
        service.SetupGet(s => s.Log).Returns(new List<string>());
        service.SetupGet(s => s.IsBusy).Returns(busy);
        service.SetupGet(s => s.ShouldOfferAtStartup).Returns(state != RuntimeDependencyState.Installed);
        return service;
    }

    [TestMethod]
    public async Task AMissingComponentSaysWhatInstallingWillDo()
    {
        var service = NewService(RuntimeDependencyState.Missing);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());

        await ShowAsync(dialog);

        // The first page is the first dependency: what it is, and that installing it is what Enter does.
        var first = Render(dialog);
        AssertScreenContains(first, "Step 1 of 2: Playwright");
        AssertScreenContains(first, "Playwright - Chromium for the quote indicator");
        AssertScreenContains(first, "Not installed. Install 1.63.0.");
        AssertScreenContains(first, "[x] Install this one");
        AssertScreenContains(first, "Enter:next  Backspace:back  Space:tick  S:don't ask again  Esc:later");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        AssertScreenContains(Render(dialog), "Step 2 of 2: Install");
        AssertScreenContains(Render(dialog), "Enter:install  Backspace:back  Space:tick  S:don't ask again  Esc:later");
        Assert.IsTrue(dialog.ShouldOpenAtStartup);
    }

    [TestMethod]
    public async Task InitializingAnOpenDialogAgain_KeepsTheFirstPageOnScreen()
    {
        var service = NewService(RuntimeDependencyState.Missing);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        // The start-up offer shows the dialog and then the screen initializes all its children, the dialog among them.
        await dialog.OnInitializeAsync();

        var screen = Render(dialog);
        AssertScreenContains(screen, "Step 1 of 2: Playwright");
        AssertScreenContains(screen, "Not installed. Install 1.63.0.");
        AssertScreenContains(screen, "[x] Install this one");
    }

    [TestMethod]
    public async Task AnUpdateSaysWhichVersionItReplaces()
    {
        var service = NewService(RuntimeDependencyState.UpdateAvailable);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());

        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "Installed 1.63.0. Update to 1.63.2.");
    }

    [TestMethod]
    public async Task EnterStartsTheInstallWhenThereIsWork()
    {
        var service = NewService(RuntimeDependencyState.Missing);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        // The component, then the summary: the second Enter installs.
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        service.Verify(s => s.StartInstall(), Times.Never);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        service.Verify(s => s.StartInstall(), Times.Once);
    }

    [TestMethod]
    public async Task EnterDoesNotStartAnotherInstallWhileOneRuns()
    {
        var service = NewService(RuntimeDependencyState.Missing, busy: true);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        service.Verify(s => s.StartInstall(), Times.Never);
        AssertScreenContains(Render(dialog), "Installing... Esc:hide this (it keeps going)");
    }

    [TestMethod]
    public async Task WhileInstalling_ADownloadsProgressShowsAsARealBar_ElseTheSweepRemains()
    {
        var service = NewService(RuntimeDependencyState.Missing, busy: true);
        List<string> log = [];
        service.SetupGet(s => s.Log).Returns(log);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        log.Add("Preparing the download");
        var before = Render(dialog);
        AssertScreenDoesNotContain(before, "%");

        log.Add("Downloading playwright.zip: 3.0 of 12.0 MB");
        dialog.OnIdle();
        var screen = Render(dialog);
        AssertScreenContains(screen, " 25%");
        AssertScreenContains(screen, "3.0 of 12.0 MB");
        AssertScreenContains(screen, "[");
    }

    [TestMethod]
    public async Task OnceTheInstallIsOver_NoBarIsLeftOnScreen()
    {
        var service = NewService(RuntimeDependencyState.Installed);
        List<string> log = ["Downloading playwright.zip: 12.0 of 12.0 MB"];
        service.SetupGet(s => s.Log).Returns(log);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        AssertScreenDoesNotContain(Render(dialog), "100%");
    }

    [TestMethod]
    public async Task SkipRemembersTheOffer()
    {
        var service = NewService(RuntimeDependencyState.UpdateAvailable);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.S, 'S'));

        service.Verify(s => s.SkipOffers(), Times.Once);
        service.Verify(s => s.StartInstall(), Times.Never);
    }

    private static Mock<IRuntimeDependencyService> NewTwoMissing()
    {
        var service = new Mock<IRuntimeDependencyService>();
        service.SetupGet(s => s.Statuses).Returns(
        [
            new RuntimeDependencyStatus("playwright", "Playwright", "Chromium", null, "1.63.0", RuntimeDependencyState.Missing),
            new RuntimeDependencyStatus("ollama", "Ollama", "Local AI", null, "0.40.4", RuntimeDependencyState.Missing),
        ]);
        service.SetupGet(s => s.Log).Returns(new List<string>());
        service.SetupGet(s => s.IsBusy).Returns(false);
        service.SetupGet(s => s.ShouldOfferAtStartup).Returns(true);
        return service;
    }

    [TestMethod]
    public async Task EachDependencyGetsItsOwnStepAndTheLastStepSumsUp()
    {
        var service = NewTwoMissing();
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "Step 1 of 3: Playwright");
        AssertScreenContains(Render(dialog), "[x] Install this one");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        AssertScreenContains(Render(dialog), "Step 2 of 3: Ollama");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        AssertScreenContains(Render(dialog), "Step 3 of 3: Install");
        AssertScreenContains(Render(dialog), "Playwright: Not installed. Install 1.63.0.");
        AssertScreenContains(Render(dialog), "Ollama: Not installed. Install 0.40.4.");
        service.Verify(s => s.StartInstall(), Times.Never);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        service.Verify(s => s.StartInstall(), Times.Once);
        Assert.IsFalse(dialog.IsWizardActive);
    }

    [TestMethod]
    public async Task BackspaceGoesBackAStep()
    {
        var service = NewTwoMissing();
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));

        AssertScreenContains(Render(dialog), "Step 2 of 3: Ollama");
    }

    [TestMethod]
    public async Task SpaceLeavesTheDependencyOnTheStepOutAndOnlyTheTickedOnesAreInstalled()
    {
        var service = NewTwoMissing();
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        AssertScreenContains(Render(dialog), "[ ] Left out");
        Assert.IsFalse(dialog.IsTicked("ollama"));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        service.Verify(s => s.StartInstall(), Times.Never);
        service.Verify(s => s.StartInstall(It.Is<IReadOnlyCollection<string>?>(ids => ids != null && ids.Count == 1 && ids.Contains("playwright"))), Times.Once);
    }

    [TestMethod]
    public async Task WithEverythingLeftOutTheSummaryRefusesToFinish()
    {
        var service = NewTwoMissing();
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        AssertScreenContains(Render(dialog), "Nothing is ticked. Go back and tick what to install.");
        service.Verify(s => s.StartInstall(), Times.Never);
        service.Verify(s => s.StartInstall(It.IsAny<IReadOnlyCollection<string>?>()), Times.Never);
    }

    [TestMethod]
    public async Task WhenNothingIsLeftTheOverviewSaysSoAndOnlyCloses()
    {
        var service = NewService(RuntimeDependencyState.Installed);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        // The Tools command does not open the dialog then, but a dialog opened with nothing to do says so and lists nothing.
        var page = Render(dialog);
        AssertScreenContains(page, "Step 1 of 1: Overview");
        AssertScreenContains(page, "All components are installed and up to date.");
        AssertScreenDoesNotContain(page, "Playwright");
        AssertScreenContains(page, "Enter:close  Backspace:back  Esc:close");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        Assert.IsTrue(dialog.IsTicked("playwright"));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        service.Verify(s => s.StartInstall(), Times.Never);
        service.Verify(s => s.StartInstall(It.IsAny<IReadOnlyCollection<string>?>()), Times.Never);
        Assert.IsFalse(dialog.ShouldOpenAtStartup);
    }

    [TestMethod]
    public async Task AnUpToDateComponentIsNeverOnTheDialogOrTheSummary()
    {
        var service = new Mock<IRuntimeDependencyService>();
        service.SetupGet(s => s.Statuses).Returns(
        [
            new RuntimeDependencyStatus("playwright", "Playwright", "Chromium", "1.63.0", null, RuntimeDependencyState.Installed),
            new RuntimeDependencyStatus("ollama", "Ollama", "Local AI", null, "0.40.4", RuntimeDependencyState.Missing),
        ]);
        service.SetupGet(s => s.Log).Returns(new List<string>());
        service.SetupGet(s => s.IsBusy).Returns(false);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        // The only page is Ollama's; the installed Playwright has no page of its own.
        var first = Render(dialog);
        AssertScreenContains(first, "Step 1 of 2: Ollama");
        AssertScreenDoesNotContain(first, "Playwright");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        var summary = Render(dialog);
        AssertScreenContains(summary, "Step 2 of 2: Install");
        AssertScreenContains(summary, "Ollama: Not installed. Install 0.40.4.");
        Assert.IsFalse(summary.Contains("Playwright"), summary.ToString());
    }

    [TestMethod]
    public async Task OnlyTheComponentsThatNeedWorkGetAPage()
    {
        var service = new Mock<IRuntimeDependencyService>();
        service.SetupGet(s => s.Statuses).Returns(
        [
            new RuntimeDependencyStatus("playwright", "Playwright", "Chromium", "1.63.0", null, RuntimeDependencyState.Installed),
            new RuntimeDependencyStatus("ollama", "Ollama", "Local AI", "0.40.3", "0.40.4", RuntimeDependencyState.UpdateAvailable),
            new RuntimeDependencyStatus("git", "Git", "Source control", "2.45.0", null, RuntimeDependencyState.Installed),
        ]);
        service.SetupGet(s => s.Log).Returns(new List<string>());
        service.SetupGet(s => s.IsBusy).Returns(false);
        service.SetupGet(s => s.ShouldOfferAtStartup).Returns(true);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        // Ollama is the only one that needs work, so it is the only page and the first one.
        var first = Render(dialog);
        AssertScreenContains(first, "Step 1 of 2: Ollama");
        AssertScreenDoesNotContain(first, "Git");
        AssertScreenDoesNotContain(first, "Playwright");
    }

    [TestMethod]
    public async Task WithNothingToCheckTheFirstPageStillSaysSo()
    {
        var service = new Mock<IRuntimeDependencyService>();
        service.SetupGet(s => s.Statuses).Returns([]);
        service.SetupGet(s => s.Log).Returns(new List<string>());
        service.SetupGet(s => s.IsBusy).Returns(false);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());

        await ShowAsync(dialog);

        var screen = Render(dialog);
        AssertScreenContains(screen, "Step 1 of 1: Overview");
        AssertScreenContains(screen, "There is nothing to set up.");
    }

    [TestMethod]
    public async Task EachDependencyIsItsOwnPageInListOrder_TheFirstPageIsTheFirstDependency()
    {
        var service = NewTwoMissing();
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        var first = Render(dialog);
        AssertScreenContains(first, "Step 1 of 3: Playwright");
        AssertScreenDoesNotContain(first, "Overview");
        AssertScreenDoesNotContain(first, "Ollama");
    }

    [TestMethod]
    public async Task ThereIsNoOverviewPageWhenThereAreDependencies()
    {
        var service = NewService(RuntimeDependencyState.UpdateAvailable);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        AssertScreenDoesNotContain(Render(dialog), "Overview");
        AssertScreenContains(Render(dialog), "Step 1 of 2: Playwright");
    }
}
