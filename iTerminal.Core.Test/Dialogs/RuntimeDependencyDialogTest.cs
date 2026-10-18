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

        var screen = Render(dialog);
        AssertScreenContains(screen, "Playwright - Chromium for the quote indicator");
        AssertScreenContains(screen, "Not installed. Install 1.63.0.");
        AssertScreenContains(screen, "Step 1 of 1: Playwright");
        AssertScreenContains(screen, "[x] Install this one");
        AssertScreenContains(screen, "Enter:install  Backspace:back  Space:tick  S:don't ask again  Esc:later");
        Assert.IsTrue(dialog.ShouldOpenAtStartup);
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
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));

        AssertScreenContains(Render(dialog), "Step 1 of 3: Playwright");
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
    public async Task WhenNothingIsLeftEnterOnlyCloses()
    {
        var service = NewService(RuntimeDependencyState.Installed);
        var dialog = new RuntimeDependencyDialog(service.Object, TestTheme());
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "Enter or Esc:close");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        service.Verify(s => s.StartInstall(), Times.Never);
        Assert.IsFalse(dialog.ShouldOpenAtStartup);
    }
}
