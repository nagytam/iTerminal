using iTerminal.DevelopmentStudio.Dialogs;
using iTerminal.DevelopmentStudio.Model.NuGet;
using iTerminal.DevelopmentStudio.Services.NuGet;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.DevelopmentStudio.Test.Dialogs;

[TestClass]
public class NuGetManagerDialogTest
{
    private static readonly string AppSolution = Path.Combine("src", "App.slnx");
    private static readonly string Web = Path.Combine("src", "Web", "Web.csproj");
    private static readonly string Core = Path.Combine("src", "Core", "Core.csproj");

    /// <summary>Web has an outdated Newtonsoft.Json; Core has the newest one and an outdated Serilog.</summary>
    private static FakeNuGetPackageService Packages() => new()
    {
        Projects = [Web, Core],
        Listing = new NuGetListing(
        [
            new NuGetPackageUsage(Web, "Newtonsoft.Json", "13.0.1", "13.0.1", "13.0.3"),
            new NuGetPackageUsage(Core, "Newtonsoft.Json", "13.0.3", "13.0.3", null),
            new NuGetPackageUsage(Core, "Serilog", "3.1.0", "3.1.0", "4.0.0"),
        ], [], Succeeded: true),
    };

    private static async Task<NuGetManagerDialog> OpenAsync(FakeNuGetPackageService packages, FakePromptService? prompts = null, string? initialProject = null)
    {
        var dialog = new NuGetManagerDialog(packages, prompts ?? new FakePromptService(), TestTheme())
        {
            TargetPath = AppSolution,
            InitialProjectPath = initialProject,
            DotnetHost = "dotnet",
        };
        await ShowAsync(dialog);
        await WaitForPackagesAsync(dialog);
        return dialog;
    }

    /// <summary>Pumps the dialog's idle pass, which is where the background work lands on the UI side.</summary>
    private static Task WaitForPackagesAsync(NuGetManagerDialog dialog) =>
        WaitUntilAsync(() => { dialog.OnIdle(); return dialog.ScopeNames().Count > 0 && !dialog.IsBusy; }, "the packages to be read");

    private static Task WaitUntilIdleAsync(NuGetManagerDialog dialog, Func<bool> condition, string because) =>
        WaitUntilAsync(() => { dialog.OnIdle(); return condition(); }, because);

    private static Task WaitForFinishAsync(NuGetManagerDialog dialog, string status) =>
        WaitUntilIdleAsync(dialog, () => !dialog.IsBusy && dialog.Status == status, $"the status \"{status}\"");

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheSolutionScopeListsItsProjectsAndGroupsEachPackageAcrossThem()
    {
        var dialog = await OpenAsync(Packages());

        CollectionAssert.AreEqual(new[] { "All projects in App", "Web", "Core" }, dialog.ScopeNames().ToArray());
        var rows = dialog.PackageRows();
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("Newtonsoft.Json", rows[0].PackageId);
        Assert.AreEqual(2, rows[0].Usages.Count, "Newtonsoft.Json is in both projects");
        Assert.AreEqual("Serilog", rows[1].PackageId);
        Assert.AreEqual("3 packages in 2 projects; 2 outdated.", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheScopeAndThePackagesAreDrawnWithTheirVersions()
    {
        var dialog = await OpenAsync(Packages());
        dialog.OnIdle();

        var screen = Render(dialog);
        AssertScreenContains(screen, "All projects in App");
        AssertScreenContains(screen, "Newtonsoft.Json");
        AssertScreenContains(screen, "13.0.3");
        AssertScreenContains(screen, "2 outdated");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AProjectOpenedOnItsOwnScopeShowsOnlyItsPackages()
    {
        var dialog = await OpenAsync(Packages(), initialProject: Core);

        Assert.AreEqual("Core", dialog.SelectedScopeName);
        var rows = dialog.PackageRows();
        CollectionAssert.AreEqual(new[] { "Newtonsoft.Json", "Serilog" }, rows.Select(row => row.PackageId).ToArray());
        Assert.IsTrue(rows.All(row => row.Usages.All(usage => usage.ProjectPath == Core)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MovingTheCursorToAProjectShowsThatProjectsPackages()
    {
        var dialog = await OpenAsync(Packages());

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await WaitUntilIdleAsync(dialog, () => dialog.SelectedScopeName == "Web", "the cursor to move to Web");

        CollectionAssert.AreEqual(new[] { "Newtonsoft.Json" }, dialog.PackageRows().Select(row => row.PackageId).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterUpdatesOnlyTheOutdatedReferencesOfThePackageUnderTheCursor()
    {
        var packages = Packages();
        var dialog = await OpenAsync(packages);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await WaitForFinishAsync(dialog, "Update Newtonsoft.Json: 1 succeeded, 0 failed.");

        CollectionAssert.AreEqual(new[] { "add Web Newtonsoft.Json 13.0.3" }, packages.Calls.ToArray());
        Assert.IsTrue(dialog.OutputLines().Any(line => line.StartsWith("$ dotnet add ", StringComparison.Ordinal) && line.EndsWith("--version 13.0.3", StringComparison.Ordinal)));
        Assert.IsTrue(dialog.OutputLines().Any(line => line.Contains("updated in file", StringComparison.Ordinal)), "the command's own output is shown");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UpdateAllAsksBeforeItChangesAnyProject()
    {
        var packages = Packages();
        var dialog = await OpenAsync(packages);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.U, 'U', shift: true));
        Assert.IsTrue(dialog.IsConfirming);
        Assert.AreEqual("Update 2 package references in 2 project(s)? (y/n)", dialog.Status);
        Assert.AreEqual(0, packages.Calls.Count, "nothing runs before the answer");

        await dialog.HandleKeyEventAsync(Char('n'));
        Assert.IsFalse(dialog.IsConfirming);
        Assert.AreEqual("Cancelled.", dialog.Status);
        Assert.AreEqual(0, packages.Calls.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UpdateAllRunsEveryOutdatedReferenceAfterAYes()
    {
        var packages = Packages();
        var dialog = await OpenAsync(packages);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.U, 'U', shift: true));
        await dialog.HandleKeyEventAsync(Char('y'));
        await WaitForFinishAsync(dialog, "Update all: 2 succeeded, 0 failed.");

        CollectionAssert.AreEqual(new[] { "add Web Newtonsoft.Json 13.0.3", "add Core Serilog 4.0.0" }, packages.Calls.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UpdateAllInAUpToDateScopeChangesNothing()
    {
        var packages = Packages();
        var dialog = await OpenAsync(packages, initialProject: Core);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter)); // Newtonsoft.Json in Core is the newest: nothing to update
        Assert.AreEqual("Newtonsoft.Json is up to date.", dialog.Status);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.U, 'U', shift: true));
        Assert.AreEqual("Update 1 package references in 1 project(s)? (y/n)", dialog.Status, "Serilog in Core is outdated, so only it");
        await dialog.HandleKeyEventAsync(Char('n'));
        Assert.AreEqual(0, packages.Calls.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DelRemovesThePackageFromEveryProjectOnlyAfterAYes()
    {
        var packages = Packages();
        var dialog = await OpenAsync(packages);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        Assert.AreEqual("Remove Newtonsoft.Json from 2 project(s)? (y/n)", dialog.Status);
        Assert.AreEqual(0, packages.Calls.Count, "nothing is removed before the answer");

        await dialog.HandleKeyEventAsync(Char('y'));
        await WaitForFinishAsync(dialog, "Remove Newtonsoft.Json: 2 succeeded, 0 failed.");

        CollectionAssert.AreEqual(new[] { "remove Web Newtonsoft.Json", "remove Core Newtonsoft.Json" }, packages.Calls.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InstallNeedsAProjectAndTakesTheIdFromAPrompt()
    {
        var packages = Packages();
        var prompts = new FakePromptService { Answer = "Dapper" };
        var solutionDialog = await OpenAsync(packages, prompts);

        await solutionDialog.HandleKeyEventAsync(Char('i'));
        Assert.AreEqual("Select a project to install into; a solution takes no package.", solutionDialog.Status);
        Assert.AreEqual(0, prompts.Questions.Count);

        var projectDialog = await OpenAsync(packages, prompts, initialProject: Core);
        await projectDialog.HandleKeyEventAsync(Char('i'));
        await WaitForFinishAsync(projectDialog, "Install Dapper: 1 succeeded, 0 failed.");

        CollectionAssert.AreEqual(new[] { "Package id to install into Core" }, prompts.Questions.ToArray());
        CollectionAssert.AreEqual(new[] { "add Core Dapper latest" }, packages.Calls.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACancelledInstallPromptChangesNothing()
    {
        var packages = Packages();
        var prompts = new FakePromptService { Answer = null };
        var dialog = await OpenAsync(packages, prompts, initialProject: Core);

        await dialog.HandleKeyEventAsync(Char('i'));
        await WaitUntilIdleAsync(dialog, () => prompts.Questions.Count == 1, "the install prompt to be shown");
        await Task.Delay(200);
        dialog.OnIdle();

        Assert.IsFalse(dialog.IsBusy, "a cancelled prompt starts nothing");
        Assert.AreEqual(0, packages.Calls.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedListingSaysWhyAndShowsNoPackages()
    {
        var packages = new FakeNuGetPackageService
        {
            Projects = [Web],
            Listing = new NuGetListing([], ["Restore failed. Run `dotnet restore` for more details on the issue."], Succeeded: false),
        };

        var dialog = await OpenAsync(packages);

        Assert.AreEqual("Restore failed. Run `dotnet restore` for more details on the issue.", dialog.Status);
        Assert.AreEqual(0, dialog.PackageRows().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACommandCannotStartWhileAnotherRuns()
    {
        var packages = Packages();
        var dialog = await OpenAsync(packages);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        // No idle pass ran between the two keys, so the update still holds the dialog busy.
        Assert.AreEqual("A command is still running; X cancels it.", dialog.Status);
        await WaitForFinishAsync(dialog, "Update Newtonsoft.Json: 1 succeeded, 0 failed.");
        Assert.AreEqual(1, packages.Calls.Count, "the Del that came during the update did not start a removal");
    }
}
