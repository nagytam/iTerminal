using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs.Git;
using iTerminal.Core.Model.Git;
using iTerminal.Core.Services.Git;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Dialogs.Git.Test;

/// <summary>The git sync dialog: pull with the chosen reconciliation, then push when ticked; a failed step stops the sync there. F6 cleans what is merged.</summary>
[TestClass]
public class GitSyncDialogTest
{
    private string _root = string.Empty;
    private List<string> _calls = [];

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "iTerminal-gitsyncdialog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "repo", ".git"));
        _calls = [];
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    private (GitSyncDialog Dialog, Mock<IGitService> Git) Create(
        string? upstream = "origin/main",
        IReadOnlyList<GitFileStatus>? unstaged = null,
        IReadOnlyList<GitWorktree>? worktrees = null,
        IReadOnlyList<string>? merged = null,
        IReadOnlyList<GitBranch>? branches = null,
        string currentBranch = "main")
    {
        Mock<IContentModel> model = new();
        model.SetupGet(m => m.Url).Returns(new Url(Path.Combine(_root, "repo")));
        Mock<IContentModelService> content = new();
        content.Setup(s => s.GetFocusedModel()).Returns(model.Object);
        Mock<IThemeService> theme = new();
        theme.SetupGet(t => t.CurrentColorScheme).Returns(new ColorScheme());

        Mock<IGitService> git = new();
        git.Setup(g => g.GetRepositoryRootAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("/the/root");
        git.Setup(g => g.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitRepositoryStatus(currentBranch, upstream, 1, 1, [], unstaged ?? []));
        git.Setup(g => g.GetBranchesAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(branches ?? [new GitBranch("main", true, "origin/main"), new GitBranch("origin/dev", false, null, IsRemote: true)]);
        git.Setup(g => g.GetWorktreesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(worktrees ?? [new GitWorktree("/the/root", "main", IsMain: true, IsLocked: false, IsPrunable: false)]);
        git.Setup(g => g.GetDefaultBranchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("main");
        git.Setup(g => g.GetMergedBranchesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(merged ?? []);
        git.Setup(g => g.PullAsync(It.IsAny<string>(), It.IsAny<GitPullMode>(), It.IsAny<CancellationToken>()))
            .Callback<string, GitPullMode, CancellationToken>((_, mode, _) => _calls.Add("pull:" + mode)).ReturnsAsync(GitResult.Ok());
        git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("push")).ReturnsAsync(GitResult.Ok());
        git.Setup(g => g.PruneWorktreesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("prune")).ReturnsAsync(GitResult.Ok());

        return (new GitSyncDialog(git.Object, content.Object, theme.Object), git);
    }

    private static async Task<GitSyncDialog> OpenAsync(GitSyncDialog dialog)
    {
        await dialog.OnInitializeAsync();
        await dialog.RefreshTask;
        return dialog;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Sync_PullsWithTheChosenModeThenPushes()
    {
        var (dialog, git) = Create();
        await OpenAsync(dialog);
        dialog.PullMode = GitPullMode.Merge;

        await dialog.SyncAsync();
        await dialog.SyncTask;

        CollectionAssert.AreEqual(new[] { "pull:Merge", "push" }, _calls);
        git.Verify(g => g.PullAsync("/the/root", GitPullMode.Merge, It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual("Synced with the remote.", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Sync_WithPushUnticked_PullsOnly()
    {
        var (dialog, _) = Create();
        await OpenAsync(dialog);
        dialog.PushAfterPull = false;

        await dialog.SyncAsync();
        await dialog.SyncTask;

        CollectionAssert.AreEqual(new[] { "pull:Rebase" }, _calls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Sync_AFailedPull_DoesNotPush_AndListsTheConflicts()
    {
        var conflicted = new[] { new GitFileStatus("src/a.cs", "UU", IsUntracked: false) };
        var (dialog, git) = Create(unstaged: conflicted);
        await OpenAsync(dialog);
        git.Setup(g => g.PullAsync(It.IsAny<string>(), It.IsAny<GitPullMode>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("pull")).ReturnsAsync(GitResult.Fail("\nerror: could not apply 1a2b3c4\nhint: resolve"));

        await dialog.SyncAsync();
        await dialog.SyncTask;

        CollectionAssert.AreEqual(new[] { "pull" }, _calls);
        StringAssert.StartsWith(dialog.Status, "Pull failed: error: could not apply 1a2b3c4");
        CollectionAssert.AreEqual(new[] { "src/a.cs" }, dialog.Conflicts.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Sync_AFailedPush_SaysGitsFirstLine()
    {
        var (dialog, git) = Create();
        await OpenAsync(dialog);
        git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitResult.Fail("! [rejected] main -> main\nmore"));

        await dialog.SyncAsync();
        await dialog.SyncTask;

        Assert.AreEqual("Push failed: ! [rejected] main -> main", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Sync_WithNoUpstream_NeitherPullsNorPushes()
    {
        var (dialog, _) = Create(upstream: null);
        await OpenAsync(dialog);

        await dialog.SyncAsync();
        await dialog.SyncTask;

        Assert.AreEqual(0, _calls.Count);
        Assert.AreEqual("The branch has no upstream branch to sync with.", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Refresh_OutsideAWorkingTree_SaysSo()
    {
        Mock<IContentModel> model = new();
        model.SetupGet(m => m.Url).Returns(new Url(Path.Combine(_root, "plain")));
        Mock<IContentModelService> content = new();
        content.Setup(s => s.GetFocusedModel()).Returns(model.Object);
        Mock<IThemeService> theme = new();
        theme.SetupGet(t => t.CurrentColorScheme).Returns(new ColorScheme());
        Directory.CreateDirectory(Path.Combine(_root, "plain"));
        var dialog = new GitSyncDialog(Mock.Of<IGitService>(), content.Object, theme.Object);

        await OpenAsync(dialog);

        Assert.AreEqual("This folder is not in a git repository.", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Refresh_ListsTheWorktreesAndTheMergedBranches()
    {
        var worktrees = new[]
        {
            new GitWorktree("/the/root", "main", IsMain: true, IsLocked: false, IsPrunable: false),
            new GitWorktree("/wt/old", "old", IsMain: false, IsLocked: false, IsPrunable: false),
        };
        var (dialog, _) = Create(worktrees: worktrees, merged: ["main", "old"]);

        await OpenAsync(dialog);

        Assert.AreEqual(2, dialog.Worktrees.Count);
        Assert.AreEqual("/wt/old", dialog.Worktrees[1].Path);
        CollectionAssert.AreEqual(new[] { "main", "old" }, dialog.MergedBranches.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Clean_FirstPressProposes_SecondPressRemovesWorktreesAndBranches()
    {
        var worktrees = new[]
        {
            new GitWorktree("/the/root", "main", IsMain: true, IsLocked: false, IsPrunable: false),
            new GitWorktree("/wt/old", "old", IsMain: false, IsLocked: false, IsPrunable: false),
            new GitWorktree("/wt/wip", "wip", IsMain: false, IsLocked: false, IsPrunable: false),
        };
        var (dialog, git) = Create(worktrees: worktrees, merged: ["main", "old", "gone"]);
        await OpenAsync(dialog);
        git.Setup(g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, CancellationToken>((_, name, _, _) => _calls.Add("delete:" + name)).ReturnsAsync(GitResult.Ok());
        git.Setup(g => g.RemoveWorktreeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, path, _) => _calls.Add("remove:" + path)).ReturnsAsync(GitResult.Ok());

        // The first press only proposes: "wip" is not merged, so its worktree stays out of the plan.
        await dialog.CleanAsync();
        Assert.IsNotNull(dialog.PendingClean);
        CollectionAssert.AreEqual(new[] { "/wt/old" }, dialog.PendingClean!.Worktrees.Select(w => w.Path).ToList());
        CollectionAssert.AreEqual(new[] { "old", "gone" }, dialog.PendingClean.Branches.ToList());
        Assert.AreEqual(0, _calls.Count, "nothing is removed before the second press");

        await dialog.CleanAsync();
        await dialog.CleanTask;

        CollectionAssert.AreEqual(new[] { "remove:/wt/old", "delete:old", "delete:gone", "prune" }, _calls);
        Assert.IsNull(dialog.PendingClean);
        Assert.AreEqual("Cleaned: removed 1 worktree(s), deleted 2 branch(es).", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Clean_ADirtyWorktree_IsRefused_AndItsBranchIsKept()
    {
        var worktrees = new[]
        {
            new GitWorktree("/the/root", "main", IsMain: true, IsLocked: false, IsPrunable: false),
            new GitWorktree("/wt/dirty", "dirty", IsMain: false, IsLocked: false, IsPrunable: false),
        };
        var (dialog, git) = Create(worktrees: worktrees, merged: ["main", "dirty"]);
        await OpenAsync(dialog);
        git.Setup(g => g.RemoveWorktreeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitResult.Fail("fatal: '/wt/dirty' contains modified or untracked files"));

        await dialog.CleanAsync();
        await dialog.CleanAsync();
        await dialog.CleanTask;

        git.Verify(g => g.DeleteBranchAsync(It.IsAny<string>(), "dirty", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        StringAssert.Contains(dialog.Status, "Not cleaned: /wt/dirty");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Clean_LeavesLockedWorktrees_AndTheBranchesAnotherWorktreeHolds()
    {
        var worktrees = new[]
        {
            new GitWorktree("/the/root", "main", IsMain: true, IsLocked: false, IsPrunable: false),
            new GitWorktree("/wt/locked", "locked-branch", IsMain: false, IsLocked: true, IsPrunable: false),
        };
        var (dialog, _) = Create(worktrees: worktrees, merged: ["main", "locked-branch"]);
        await OpenAsync(dialog);

        await dialog.CleanAsync();

        Assert.IsNull(dialog.PendingClean, "a locked worktree is not removed, and the branch it holds stays with it");
        Assert.AreEqual("Nothing merged into main to clean.", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Clean_WithNothingMerged_ProposesNothing()
    {
        var (dialog, _) = Create(merged: ["main"]);
        await OpenAsync(dialog);

        await dialog.CleanAsync();

        Assert.IsNull(dialog.PendingClean);
        Assert.AreEqual("Nothing merged into main to clean.", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Clean_AnyKeyOtherThanF6OrEscape_WithdrawsTheProposal()
    {
        var (dialog, git) = Create(merged: ["main", "old"], worktrees: [new GitWorktree("/the/root", "main", IsMain: true, IsLocked: false, IsPrunable: false)]);
        await OpenAsync(dialog);

        await dialog.CleanAsync();
        Assert.IsNotNull(dialog.PendingClean);

        await dialog.HandleKeyEventAsync(new KeyEvent('x', ConsoleKey.X, false, false, false));

        Assert.IsNull(dialog.PendingClean);
        git.Verify(g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
