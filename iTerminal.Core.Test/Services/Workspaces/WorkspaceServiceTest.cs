using iTerminal.Core.Data;
using iTerminal.Core.Test.Model.Map;
using iTerminal.Core.Model.Workspaces;
using iTerminal.Core.Services.Workspaces;

namespace iTerminal.Core.Test.Services.Workspaces;

internal sealed class FakeLayout : IWorkspaceLayoutService
{
    public WorkspaceLayout Current { get; set; } = new(@"file://C:\work\app", @"file://C:\work\docs", "AsListed", "Name", WorkspaceBar.Ai, "LeftPanel");
    public List<WorkspaceLayout> Applied { get; } = [];
    public List<string> Problems { get; } = [];

    public WorkspaceLayout Capture() => Current;

    public Task<IReadOnlyList<string>> ApplyAsync(WorkspaceLayout layout)
    {
        Applied.Add(layout);
        Current = layout;
        return Task.FromResult<IReadOnlyList<string>>([.. Problems]);
    }
}

[TestClass]
public class WorkspaceServiceTest
{
    private static (WorkspaceService Service, FakeLayout Layout, MemorySettings Settings) Create()
    {
        var settings = new MemorySettings();
        var layout = new FakeLayout();
        return (new WorkspaceService(settings, layout), layout, settings);
    }

    [TestMethod]
    public void NothingIsSavedAtFirst()
    {
        var (service, _, _) = Create();

        Assert.AreEqual(0, service.GetAll().Count);
        Assert.IsNull(service.ActiveName);
        Assert.AreEqual("Workspace 1", service.SuggestName());
    }

    [TestMethod]
    public void SavingKeepsTheScreenAsItIsAndMarksItActive()
    {
        var (service, layout, _) = Create();

        var saved = service.Save("  Project  ");

        Assert.AreEqual("Project", saved.Name);
        Assert.AreEqual(layout.Current, saved.Layout);
        Assert.AreEqual("Project", service.ActiveName);
        Assert.AreEqual(1, service.GetAll().Count);
        Assert.AreEqual("Workspace 1", service.SuggestName());
    }

    [TestMethod]
    public void WorkspacesSurviveANewServiceOverTheSameSettings()
    {
        var (service, layout, settings) = Create();
        layout.Current = layout.Current with { Bar = WorkspaceBar.Terminal, LeftSort = "Size,desc" };
        service.Save("Build");

        var reloaded = new WorkspaceService(settings, layout).GetAll();

        Assert.AreEqual(1, reloaded.Count);
        Assert.AreEqual(WorkspaceBar.Terminal, reloaded[0].Layout.Bar);
        Assert.AreEqual("Size,desc", reloaded[0].Layout.LeftSort);
    }

    [TestMethod]
    public void SavingUnderAnExistingNameReplacesItInPlace()
    {
        var (service, layout, _) = Create();
        service.Save("One");
        service.Save("Two");

        layout.Current = layout.Current with { LeftUrl = @"file://C:\elsewhere" };
        service.Save("one");

        var all = service.GetAll();
        Assert.AreEqual(2, all.Count);
        Assert.AreEqual("One", all[0].Name);
        Assert.AreEqual(@"file://C:\elsewhere", all[0].Layout.LeftUrl);
        Assert.AreEqual("Two", all[1].Name);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ABlankNameIsRefused(string name)
    {
        var (service, _, _) = Create();

        Assert.IsNotNull(service.ValidateName(name));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Save(name));
    }

    [TestMethod]
    public void ANameThatIsTooLongOrHasControlCharactersIsRefused()
    {
        var (service, _, _) = Create();

        Assert.IsNotNull(service.ValidateName(new string('x', IWorkspaceService.MaxNameLength + 1)));
        Assert.IsNotNull(service.ValidateName("a\tb"));
        Assert.IsNull(service.ValidateName(new string('x', IWorkspaceService.MaxNameLength)));
    }

    [TestMethod]
    public void ATakenNameIsRefusedUnlessItIsTheOneBeingRenamed()
    {
        var (service, _, _) = Create();
        service.Save("Alpha");
        service.Save("Beta");

        Assert.IsNotNull(service.ValidateName("beta"));
        Assert.IsNull(service.ValidateName("ALPHA", renaming: "Alpha"));
        Assert.IsNotNull(service.ValidateName("Beta", renaming: "Alpha"));
    }

    [TestMethod]
    public void RenamingKeepsPositionAndActiveMark()
    {
        var (service, _, _) = Create();
        service.Save("Alpha");
        service.Save("Beta");

        service.Rename("Beta", "Gamma");

        Assert.AreEqual("Alpha", service.GetAll()[0].Name);
        Assert.AreEqual("Gamma", service.GetAll()[1].Name);
        Assert.AreEqual("Gamma", service.ActiveName);
    }

    [TestMethod]
    public void DeletingTheActiveOneLeavesNoneActive()
    {
        var (service, _, _) = Create();
        service.Save("Alpha");

        service.Delete("alpha");

        Assert.AreEqual(0, service.GetAll().Count);
        Assert.IsNull(service.ActiveName);
    }

    [TestMethod]
    public async Task RestoringPutsTheSavedLayoutOnScreenAndMarksItActive()
    {
        var (service, layout, _) = Create();
        var original = layout.Current;
        service.Save("Home");
        layout.Current = layout.Current with { LeftUrl = @"file://C:\other", Bar = WorkspaceBar.FullTerminal };
        service.Save("Away");

        var problems = await service.RestoreAsync("home");

        Assert.AreEqual(0, problems.Count);
        Assert.AreEqual(original, layout.Applied.Single());
        Assert.AreEqual("Home", service.ActiveName);
    }

    [TestMethod]
    public async Task RestoringReportsWhatCouldNotComeBack()
    {
        var (service, layout, _) = Create();
        service.Save("Home");
        layout.Problems.Add("Left panel: C:\\gone no longer exists.");

        var problems = await service.RestoreAsync("Home");

        CollectionAssert.AreEqual(new[] { "Left panel: C:\\gone no longer exists." }, problems.ToArray());
    }

    [TestMethod]
    public async Task RestoringAnUnknownNameSaysSoAndChangesNothing()
    {
        var (service, layout, _) = Create();

        var problems = await service.RestoreAsync("Nope");

        Assert.AreEqual(1, problems.Count);
        Assert.AreEqual(0, layout.Applied.Count);
    }

    [TestMethod]
    public void IsCurrentIgnoresWhereTheCursorIs()
    {
        var (service, layout, _) = Create();
        var saved = service.Save("Home");

        layout.Current = layout.Current with { FocusedPanel = "RightPanel" };
        Assert.IsTrue(service.IsCurrent(saved));

        layout.Current = layout.Current with { RightUrl = @"file://C:\moved" };
        Assert.IsFalse(service.IsCurrent(saved));
    }

    [TestMethod]
    public void BrokenJsonIsAnEmptyList()
    {
        var (service, _, settings) = Create();
        settings.Set(Settings.Workspaces, "{not json");

        Assert.AreEqual(0, service.GetAll().Count);
    }
}
