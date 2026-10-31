using iConsole.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.RuntimeDependencies;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>The three pages of the runtime dependencies wizard: the overview, one page per component and the summary.</summary>
[TestClass]
public class RuntimeDependencyPagesTest
{
    private static RuntimeDependencyStatus Missing(string id = "playwright") =>
        new(id, "Playwright", "Chromium for the quote indicator", null, "1.63.0", RuntimeDependencyState.Missing);

    private static RuntimeDependencyStatus UpdateAvailable() =>
        new("playwright", "Playwright", "Chromium for the quote indicator", "1.63.0", "1.63.2", RuntimeDependencyState.UpdateAvailable);

    private static RuntimeDependencyStatus Installed() =>
        new("playwright", "Playwright", "Chromium for the quote indicator", "1.63.0", null, RuntimeDependencyState.Installed);

    private static Mock<IRuntimeDependencyService> ServiceWith(params RuntimeDependencyStatus[] statuses)
    {
        var service = new Mock<IRuntimeDependencyService>();
        service.SetupGet(s => s.Statuses).Returns(statuses);
        return service;
    }

    [TestMethod]
    public void StateText_SaysWhatIsMissingWhatIsOutdatedAndWhatIsCurrent()
    {
        Assert.AreEqual("Not installed. Install 1.63.0.", RuntimeDependencyStepPage.StateText(Missing()));
        Assert.AreEqual("Installed 1.63.0. Update to 1.63.2.", RuntimeDependencyStepPage.StateText(UpdateAvailable()));
        Assert.AreEqual("Installed 1.63.0. Up to date.", RuntimeDependencyStepPage.StateText(Installed()));
    }

    [TestMethod]
    public void Overview_NamesNoComponent_TheDialogStepsThroughThoseThatNeedWork()
    {
        var page = new RuntimeDependencyOverviewPage(ServiceWith(Missing()).Object, TestTheme())
        {
            ViewSize = new Size(100, 8),
        };

        var screen = Render(page, new Size(100, 8));

        AssertScreenDoesNotContain(screen, "Playwright", "the components that need work get their own pages");
        AssertScreenContains(screen, "1 need to be installed or updated.");
    }

    [TestMethod]
    public void Overview_CountsTheComponentsThatNeedWork()
    {
        var page = new RuntimeDependencyOverviewPage(ServiceWith(Missing(), Installed()).Object, TestTheme())
        {
            ViewSize = new Size(100, 8),
        };

        AssertScreenContains(Render(page, new Size(100, 8)), "1 of 2 need to be installed or updated.");
    }

    [TestMethod]
    public void Overview_SaysSoWhenEveryComponentIsInstalled()
    {
        var page = new RuntimeDependencyOverviewPage(ServiceWith(Installed()).Object, TestTheme())
        {
            ViewSize = new Size(100, 8),
        };

        AssertScreenContains(Render(page, new Size(100, 8)), "All components are installed and up to date.");
    }

    [TestMethod]
    public void Overview_SaysThereIsNothingToSetUpWithoutAnyComponent()
    {
        var page = new RuntimeDependencyOverviewPage(ServiceWith().Object, TestTheme())
        {
            ViewSize = new Size(100, 8),
        };

        AssertScreenContains(Render(page, new Size(100, 8)), "There is nothing to set up.");
    }

    [TestMethod]
    public void Summary_SaysNothingIsTickedWhenThePersonLeftEverythingOut()
    {
        var page = new RuntimeDependencySummaryPage(ServiceWith(Missing()).Object, () => [], TestTheme())
        {
            ViewSize = new Size(100, 8),
        };

        AssertScreenContains(Render(page, new Size(100, 8)), "Nothing is ticked.");
    }

    [TestMethod]
    public void Summary_ListsTheTickedComponentsWithWhatFinishWillDo()
    {
        var page = new RuntimeDependencySummaryPage(ServiceWith(Missing()).Object, () => ["playwright"], TestTheme())
        {
            ViewSize = new Size(100, 8),
        };

        var screen = Render(page, new Size(100, 8));

        AssertScreenContains(screen, "Finish will install:");
        AssertScreenContains(screen, "Playwright: Not installed. Install 1.63.0.", "the ticked component says what installing does");
    }

    [TestMethod]
    public void Step_ShowsTheStateAndTheTickBox_ForAComponentThatNeedsWork()
    {
        var page = new RuntimeDependencyStepPage("playwright", ServiceWith(Missing()).Object, _ => true, TestTheme())
        {
            ViewSize = new Size(100, 5),
        };

        var screen = Render(page, new Size(100, 5));

        AssertScreenContains(screen, "Not installed. Install 1.63.0.");
        AssertScreenContains(screen, "[x] Install this one", "a ticked component says it will be installed");
    }

    [TestMethod]
    public void Step_LeavesOutAComponentThatIsUnticked()
    {
        var page = new RuntimeDependencyStepPage("playwright", ServiceWith(Missing()).Object, _ => false, TestTheme())
        {
            ViewSize = new Size(100, 5),
        };

        AssertScreenContains(Render(page, new Size(100, 5)), "[ ] Left out", "an unticked component says it is left out");
    }

    [TestMethod]
    public void Step_HasNothingToDoForAnInstalledComponent()
    {
        var page = new RuntimeDependencyStepPage("playwright", ServiceWith(Installed()).Object, _ => true, TestTheme())
        {
            ViewSize = new Size(100, 5),
        };

        AssertScreenContains(Render(page, new Size(100, 5)), "Nothing to do here.");
    }

    [TestMethod]
    public void Step_DrawsNothingWhenTheComponentIsNotInTheSnapshot()
    {
        var page = new RuntimeDependencyStepPage("missing-id", ServiceWith(Installed()).Object, _ => true, TestTheme())
        {
            ViewSize = new Size(100, 5),
        };

        var screen = Render(page, new Size(100, 5));

        AssertScreenDoesNotContain(screen, "Playwright");
    }
}
