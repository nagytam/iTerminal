using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.Git;
using iTerminal.Core.Services.Git;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Test;

/// <summary>Git sync: pull --rebase, then push; a failed pull or a branch with no upstream stops before the push.</summary>
[TestClass]
public class GitSyncCommandTest
{
    private string _root = string.Empty;
    private List<string> _calls = [];

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "iTerminal-gitsync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "repo", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "plain"));
        _calls = [];
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    private (GitSyncCommand Command, Mock<IGitService> Git, Mock<INotificationService> Notes) Create(string folder, string? upstream = "origin/main", string? repositoryRoot = "/repo")
    {
        Mock<IContentModel> model = new();
        model.SetupGet(m => m.Url).Returns(new Url(folder));
        Mock<IContentModelService> service = new();
        service.Setup(s => s.GetFocusedModel()).Returns(model.Object);
        Mock<IGitService> git = new();
        git.Setup(g => g.GetRepositoryRootAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(repositoryRoot);
        git.Setup(g => g.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitRepositoryStatus("main", upstream, 1, 1, [], []));
        git.Setup(g => g.PullRebaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("pull")).ReturnsAsync(GitResult.Ok());
        git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("push")).ReturnsAsync(GitResult.Ok());
        Mock<INotificationService> notes = new();
        return (new GitSyncCommand(service.Object, git.Object, notes.Object), git, notes);
    }

    [TestMethod]
    public void CanExecute_OnlyInsideAWorkingTree()
    {
        Assert.IsTrue(Create(Path.Combine(_root, "repo")).Command.CanExecute());
        Assert.IsFalse(Create(Path.Combine(_root, "plain")).Command.CanExecute());
    }

    [TestMethod]
    public async Task Execute_PullsThenPushesInTheRepositoryRoot()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "repo"), repositoryRoot: "/the/root");

        await command.ExecuteAsync(null);
        await command.SyncTask;

        CollectionAssert.AreEqual(new[] { "pull", "push" }, _calls);
        git.Verify(g => g.PullRebaseAsync("/the/root", It.IsAny<CancellationToken>()), Times.Once);
        git.Verify(g => g.PushAsync("/the/root", false, It.IsAny<CancellationToken>()), Times.Once);
        notes.Verify(n => n.Notify("Git", "Synced with the remote.", NotificationSeverity.Info, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_AFailedPull_DoesNotPushAndWarnsAboutTheRebase()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "repo"));
        git.Setup(g => g.PullRebaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("pull")).ReturnsAsync(GitResult.Fail("\nerror: could not apply 1a2b3c4\nhint: resolve"));

        await command.ExecuteAsync(null);
        await command.SyncTask;

        CollectionAssert.AreEqual(new[] { "pull" }, _calls);
        notes.Verify(n => n.Notify("Git sync: pull --rebase failed", "error: could not apply 1a2b3c4 (Nothing was pushed. If git started a rebase, it is left unfinished.)", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_AFailedPush_SaysGitsFirstLine()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "repo"));
        git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitResult.Fail("! [rejected] main -> main\nmore"));

        await command.ExecuteAsync(null);
        await command.SyncTask;

        notes.Verify(n => n.Notify("Git sync: push failed", "! [rejected] main -> main", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_WithNoUpstream_NeitherPullsNorPushes()
    {
        var (command, _, notes) = Create(Path.Combine(_root, "repo"), upstream: null);

        await command.ExecuteAsync(null);
        await command.SyncTask;

        Assert.AreEqual(0, _calls.Count);
        notes.Verify(n => n.Notify("Git sync", "The branch has no upstream branch to sync with.", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_AServiceThatThrows_IsSaid()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "repo"));
        git.Setup(g => g.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("git is not installed"));

        await command.ExecuteAsync(null);
        await command.SyncTask;

        notes.Verify(n => n.Notify("Git sync failed", "git is not installed", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_WhenGitSaysItIsNoRepository_DoesNothingMore()
    {
        var (command, _, notes) = Create(Path.Combine(_root, "repo"), repositoryRoot: null);

        await command.ExecuteAsync(null);
        await command.SyncTask;

        Assert.AreEqual(0, _calls.Count);
        notes.Verify(n => n.Notify("Git sync", "This folder is not in a git repository.", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_OutsideAWorkingTree_DoesNothing()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "plain"));

        await command.ExecuteAsync(null);
        await command.SyncTask;

        git.VerifyNoOtherCalls();
        notes.VerifyNoOtherCalls();
    }
}
