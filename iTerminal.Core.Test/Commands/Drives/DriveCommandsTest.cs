using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands.Drives;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Docker;
using iTerminal.Core.Services.Git;
using iTerminal.Core.Test.Helper;
using Moq;

namespace iTerminal.Core.Test.Commands.Drives;

/// <summary>
/// Ctrl+F6 and Ctrl+F9 against a real <see cref="ContentModelService"/>: the panel goes to the
/// drive, and the same key brings it back to the folder it came from.
/// </summary>
[TestClass]
public class DriveCommandsTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void CreateTree()
    {
        _root = Path.Combine(Path.GetTempPath(), "drivecmd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "repo", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "repo", "src", "deep"));
        Directory.CreateDirectory(Path.Combine(_root, "worktree"));
        File.WriteAllText(Path.Combine(_root, "worktree", ".git"), "gitdir: elsewhere");
        Directory.CreateDirectory(Path.Combine(_root, "plain"));
    }

    [TestCleanup]
    public void DeleteTree()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private static IFrequentExecutionGuardService ImmediateGuard()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        return guard.Object;
    }

    private static ContentModelService CreateService(FakeProcessRunner runner)
    {
        var settings = new SettingsService();
        var service = new ContentModelService(
            new iConsole.Tasks.TaskManager(),
            settings,
            new FileSystemService(),
            new MemoryDriveService(),
            Mock.Of<ISshFileSystemService>(),
            Mock.Of<ISmbFileSystemService>(),
            Mock.Of<IIPhoneFileSystemService>(),
            Mock.Of<INetworkProfileService>(),
            Mock.Of<IPasswordManager>(),
            Mock.Of<IArchiveFileSystemService>(),
            new FolderHistoryService(settings),
            ImmediateGuard(),
            new MemoryCacheService(),
            Mock.Of<Serilog.ILogger>(),
            gitDriveService: new GitDriveService(runner, settings),
            dockerDriveService: new DockerDriveService(runner));

        foreach (var name in new[] { "LeftPanel", "RightPanel" })
        {
            var browser = new Mock<IContentBrowser>();
            browser.SetupGet(b => b.Name).Returns(name);
            browser.Setup(b => b.ChangeModelAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
            service.AddContentBrowser(name, browser.Object);
        }

        return service;
    }

    [TestMethod]
    public void TheRepositoryIsFoundFromAnyFolderInsideIt()
    {
        var expected = Path.Combine(_root, "repo");

        Assert.AreEqual(expected, GitDriveCommand.FindRepositoryRoot(Path.Combine(_root, "repo", "src", "deep")));
        Assert.AreEqual(expected, GitDriveCommand.FindRepositoryRoot(expected));
        Assert.AreEqual(Path.Combine(_root, "worktree"), GitDriveCommand.FindRepositoryRoot(Path.Combine(_root, "worktree")), "a .git file marks a worktree");
    }

    [TestMethod]
    public async Task CtrlF9GoesToTheContainersAndBack()
    {
        var runner = new FakeProcessRunner().Answer("docker ps", "web\trunning\tUp\tnginx\t2026-10-04 08:00:00 +0200 CEST\n");
        var service = CreateService(runner);
        var home = new Url(Path.Combine(_root, "plain") + Path.DirectorySeparatorChar);
        service.AddContentModel("LeftPanel", home);
        service.AddContentModel("RightPanel", home);
        service.FocusedPanel = "LeftPanel";
        var command = new DockerDriveCommand(service);

        await command.ExecuteAsync(null);
        var docker = (ContentModelDockerDrive)service.GetFocusedModel()!;
        await docker.ListingTask;

        CollectionAssert.AreEqual(new[] { "..", "web" }, docker.Entries.AsSnapshot().Select(e => e.DisplayName).ToArray());
        Assert.AreEqual(home.FullUrl, docker.Entries[0].Url.FullUrl, "the root's .. leads back to the folder left");

        await command.ExecuteAsync(null);

        Assert.AreEqual(home.FullUrl, service.GetFocusedModel()!.Url.FullUrl);
        Assert.IsInstanceOfType<ContentModelFileSystem>(service.GetFocusedModel());
    }

    [TestMethod]
    public async Task CtrlF6InARepositoryOpensItsRevisions()
    {
        var runner = new FakeProcessRunner()
            .Answer("git -C", string.Empty);
        var service = CreateService(runner);
        service.AddContentModel("LeftPanel", new Url(Path.Combine(_root, "repo", "src") + Path.DirectorySeparatorChar));
        service.AddContentModel("RightPanel", new Url(Path.Combine(_root, "plain") + Path.DirectorySeparatorChar));
        service.FocusedPanel = "LeftPanel";

        await new GitDriveCommand(service).ExecuteAsync(null);

        var model = service.GetFocusedModel()!;
        GitDriveUrl.Parse(model.Url, out var repository, out var revision, out _);
        Assert.AreEqual(Path.Combine(_root, "repo"), repository);
        Assert.IsNull(revision, "the panel lands on the list of revisions");
    }

    [TestMethod]
    public async Task CtrlF6OutsideARepositoryOpensTheDriveRoot()
    {
        var service = CreateService(new FakeProcessRunner());
        service.AddContentModel("LeftPanel", new Url(Path.Combine(_root, "plain") + Path.DirectorySeparatorChar));
        service.AddContentModel("RightPanel", new Url(Path.Combine(_root, "plain") + Path.DirectorySeparatorChar));
        service.FocusedPanel = "LeftPanel";

        await new GitDriveCommand(service).ExecuteAsync(null);

        Assert.AreEqual(Container.GIT, service.GetFocusedModel()!.Url.FullUrl);
    }

    [TestMethod]
    public async Task CopyAndDeleteFindTheDrivesFileSystemThroughItsPanel()
    {
        var runner = new FakeProcessRunner().Answer("docker ps", string.Empty);
        var service = CreateService(runner);
        service.AddContentModel("LeftPanel", new Url(Container.DOCKER));

        var provider = service.ContentModels.OfType<IFileSystemProvider>().Single();

        Assert.AreEqual(ContainerType.Docker, provider.ContainerType);
        Assert.IsInstanceOfType<DockerDriveService>(provider.FileSystem);
        await Task.CompletedTask;
    }
}
