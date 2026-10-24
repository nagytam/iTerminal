using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Services.Git;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Commands.Test;

/// <summary>Git pull --rebase: offered only in a working tree, run through the git service, outcome always said.</summary>
[TestClass]
public class GitPullRebaseCommandTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "iTerminal-pullrebase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "repo", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "repo", "src", "deep"));
        Directory.CreateDirectory(Path.Combine(_root, "plain"));
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    private (GitPullRebaseCommand Command, Mock<IGitService> Git, Mock<INotificationService> Notes) Create(string folder, string? repositoryRoot = "/repo", ContainerType container = ContainerType.FileSystem)
    {
        Mock<IContentModel> model = new();
        model.SetupGet(m => m.Url).Returns(container == ContainerType.FileSystem ? new Url(folder) : new Url("ftp://host/" + Path.GetFileName(folder)));
        Mock<IContentModelService> service = new();
        service.Setup(s => s.GetFocusedModel()).Returns(model.Object);
        Mock<IGitService> git = new();
        git.Setup(g => g.GetRepositoryRootAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(repositoryRoot);
        git.Setup(g => g.PullRebaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(GitResult.Ok());
        Mock<INotificationService> notes = new();
        return (new GitPullRebaseCommand(service.Object, git.Object, notes.Object), git, notes);
    }

    [TestMethod]
    public void CanExecute_InTheWorkingTreeRootAndInASubfolder()
    {
        Assert.IsTrue(Create(Path.Combine(_root, "repo")).Command.CanExecute());
        Assert.IsTrue(Create(Path.Combine(_root, "repo", "src", "deep")).Command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_IsFalseOutsideAWorkingTree()
    {
        Assert.IsFalse(Create(Path.Combine(_root, "plain")).Command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_IsFalseForAFolderThatIsGone()
    {
        Assert.IsFalse(Create(Path.Combine(_root, "repo", "removed")).Command.CanExecute());
    }

    [TestMethod]
    public void CanExecute_IsFalseOnARemoteDrive()
    {
        Assert.IsFalse(Create(Path.Combine(_root, "repo"), container: ContainerType.FtpFileSystem).Command.CanExecute());
    }

    [TestMethod]
    public void IsInWorkingTree_AcceptsAGitFileAsAWorktreeOrSubmoduleHas()
    {
        string worktree = Path.Combine(_root, "worktree");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../repo/.git/worktrees/w");
        Assert.IsTrue(GitPullRebaseCommand.IsInWorkingTree(worktree));
    }

    [TestMethod]
    public async Task Execute_PullsWithRebaseInTheRepositoryRootAndSaysSo()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "repo", "src"), repositoryRoot: "/the/root");

        await command.ExecuteAsync(null);
        await command.PullTask;

        git.Verify(g => g.PullRebaseAsync("/the/root", It.IsAny<CancellationToken>()), Times.Once);
        git.Verify(g => g.PullAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        notes.Verify(n => n.Notify("Git", "Pulled with rebase.", NotificationSeverity.Info, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_AFailedPull_SaysGitsFirstLine()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "repo"));
        git.Setup(g => g.PullRebaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitResult.Fail("\nerror: could not apply 1a2b3c4\nhint: resolve all conflicts"));

        await command.ExecuteAsync(null);
        await command.PullTask;

        notes.Verify(n => n.Notify("Git pull --rebase failed", "error: could not apply 1a2b3c4", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_AFailureWithNoOutput_StillSaysSomething()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "repo"));
        git.Setup(g => g.PullRebaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(GitResult.Fail(string.Empty));

        await command.ExecuteAsync(null);
        await command.PullTask;

        notes.Verify(n => n.Notify("Git pull --rebase failed", "git returned an error.", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_AServiceThatThrows_IsSaidAsAFailedPull()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "repo"));
        git.Setup(g => g.PullRebaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("git is not installed"));

        await command.ExecuteAsync(null);
        await command.PullTask;

        notes.Verify(n => n.Notify("Git pull --rebase failed", "git is not installed", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_WhenGitSaysItIsNoRepository_DoesNotPull()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "repo"), repositoryRoot: null);

        await command.ExecuteAsync(null);
        await command.PullTask;

        git.Verify(g => g.PullRebaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        notes.Verify(n => n.Notify("Git pull --rebase", "This folder is not in a git repository.", NotificationSeverity.Warning, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [TestMethod]
    public async Task Execute_OutsideAWorkingTree_DoesNothing()
    {
        var (command, git, notes) = Create(Path.Combine(_root, "plain"));

        await command.ExecuteAsync(null);
        await command.PullTask;

        git.VerifyNoOtherCalls();
        notes.VerifyNoOtherCalls();
    }
}
