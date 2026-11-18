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

/// <summary>The git sync dialog: pull with the chosen reconciliation, then push when ticked; a failed step stops the sync there.</summary>
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

    private (GitSyncDialog Dialog, Mock<IGitService> Git) Create(string? upstream = "origin/main", IReadOnlyList<GitFileStatus>? unstaged = null, INotificationService? notifications = null)
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
            .ReturnsAsync(new GitRepositoryStatus("main", upstream, 1, 1, [], unstaged ?? []));
        git.Setup(g => g.GetBranchesAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GitBranch("main", true, "origin/main"), new GitBranch("origin/dev", false, null, IsRemote: true)]);
        git.Setup(g => g.PullAsync(It.IsAny<string>(), It.IsAny<GitPullMode>(), It.IsAny<CancellationToken>()))
            .Callback<string, GitPullMode, CancellationToken>((_, mode, _) => _calls.Add("pull:" + mode)).ReturnsAsync(GitResult.Ok());
        git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("push")).ReturnsAsync(GitResult.Ok());

        return (new GitSyncDialog(git.Object, content.Object, theme.Object, notifications), git);
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
    public async Task Sync_ReportsSuccessAsAToast()
    {
        Mock<INotificationService> notifications = new();
        var (dialog, _) = Create(notifications: notifications.Object);
        await OpenAsync(dialog);

        await dialog.SyncAsync();
        await dialog.SyncTask;

        notifications.Verify(n => n.Notify("Git sync", "Synced with the remote.", NotificationSeverity.Success), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Sync_ReportsAFailedPullAsAnErrorToast()
    {
        Mock<INotificationService> notifications = new();
        var (dialog, git) = Create(notifications: notifications.Object);
        await OpenAsync(dialog);
        git.Setup(g => g.PullAsync(It.IsAny<string>(), It.IsAny<GitPullMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitResult.Fail("error: could not apply 1a2b3c4"));

        await dialog.SyncAsync();
        await dialog.SyncTask;

        notifications.Verify(n => n.Notify("Git sync", "Pull failed: error: could not apply 1a2b3c4 Nothing was pushed. If git started a rebase or merge, it is left unfinished.", NotificationSeverity.Error), Times.Once);
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
        Assert.AreEqual("Pull failed: error: could not apply 1a2b3c4", dialog.Status);
        Assert.AreEqual("Nothing was pushed. If git started a rebase or merge, it is left unfinished.", dialog.StatusDetail);
        CollectionAssert.AreEqual(new[] { "src/a.cs" }, dialog.Conflicts.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Sync_WhileOneIsRunning_SaysSoAndStartsNoSecondPull()
    {
        var (dialog, git) = Create();
        await OpenAsync(dialog);
        var release = new TaskCompletionSource();
        git.Setup(g => g.PullAsync(It.IsAny<string>(), It.IsAny<GitPullMode>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await release.Task;
                return GitResult.Ok();
            });

        // Not awaited: SyncAsync returns the running task, which waits for the gate.
        _ = dialog.SyncAsync();
        _ = dialog.SyncAsync();

        Assert.AreEqual("A sync or branch switch is still running.", dialog.Status);
        release.SetResult();
        await dialog.SyncTask;
        git.Verify(g => g.PullAsync(It.IsAny<string>(), It.IsAny<GitPullMode>(), It.IsAny<CancellationToken>()), Times.Once);
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

    /// <summary>A repository with main, the checked-out feature, one merged and one unmerged local branch, and a merged remote branch.</summary>
    private static void SetUpBranchesForCleanup(Mock<IGitService> git)
    {
        git.Setup(g => g.GetMainBranchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("main");
        git.Setup(g => g.GetBranchesAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new GitBranch("main", false, "origin/main"),
                new GitBranch("feature/done", false, null),
                new GitBranch("feature/open", false, null),
                new GitBranch("checked-out", true, null),
                new GitBranch("origin/feature/done", false, null, IsRemote: true),
            ]);
        git.Setup(g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(GitResult.Ok());
        git.Setup(g => g.GetMergedRefsAsync(It.IsAny<string>(), "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal)
            {
                "refs/heads/main", "refs/heads/feature/done", "refs/heads/checked-out", "refs/remotes/origin/feature/done",
            });
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MergedCleanupCandidates_KeepsMainTheCheckedOutBranchAndUnmergedAndRemoteOnes()
    {
        IReadOnlyList<GitBranch> branches =
        [
            new GitBranch("main", false, null),
            new GitBranch("feature/done", false, null),
            new GitBranch("feature/open", false, null),
            new GitBranch("checked-out", true, null),
            new GitBranch("origin/feature/done", false, null, IsRemote: true),
        ];
        var merged = new HashSet<string>(StringComparer.Ordinal)
        {
            "refs/heads/main", "refs/heads/feature/done", "refs/heads/checked-out", "refs/remotes/origin/feature/done",
        };

        var names = GitSyncDialog.MergedCleanupCandidates(branches, merged, "main", currentBranch: "checked-out");

        CollectionAssert.AreEqual(new[] { "feature/done" }, names.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Cleanup_DeletesNothingUntilTheAnswerIsYes_ThenDeletesTheMergedLocalBranches()
    {
        var (dialog, git) = Create();
        SetUpBranchesForCleanup(git);
        await OpenAsync(dialog);

        dialog.BeginCleanup();

        Assert.AreEqual("Delete 1 local branch(es) merged into main? (y/n)", dialog.Status);
        Assert.AreEqual("feature/done", dialog.StatusDetail);
        git.Verify(g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

        await dialog.HandleKeyEventAsync(new KeyEvent('y', ConsoleKey.Y, false, false, false));
        await dialog.SyncTask;

        git.Verify(g => g.DeleteBranchAsync("/the/root", "feature/done", true, It.IsAny<CancellationToken>()), Times.Once);
        git.Verify(g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual("Deleted 1 merged branch(es).", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Cleanup_AnsweredNo_KeepsEverything()
    {
        var (dialog, git) = Create();
        SetUpBranchesForCleanup(git);
        await OpenAsync(dialog);

        dialog.BeginCleanup();
        await dialog.HandleKeyEventAsync(new KeyEvent('n', ConsoleKey.N, false, false, false));

        git.Verify(g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.AreEqual("Cancelled.", dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Cleanup_HotKeyAltC_AsksToo()
    {
        var (dialog, git) = Create();
        SetUpBranchesForCleanup(git);
        await OpenAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('c', ConsoleKey.C, false, true, false));

        Assert.AreEqual("Delete 1 local branch(es) merged into main? (y/n)", dialog.Status);
        git.Verify(g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Cleanup_WithNothingMerged_SaysSoAndAsksNothing()
    {
        var (dialog, git) = Create();
        SetUpBranchesForCleanup(git);
        git.Setup(g => g.GetMergedRefsAsync(It.IsAny<string>(), "main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.Ordinal) { "refs/heads/main" });
        await OpenAsync(dialog);

        dialog.BeginCleanup();

        Assert.AreEqual("No local branch is merged into main, so there is nothing to clean up.", dialog.Status);
        git.Verify(g => g.DeleteBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Cleanup_AFailedDelete_IsReportedWithTheBranchesItKept()
    {
        Mock<INotificationService> notifications = new();
        var (dialog, git) = Create(notifications: notifications.Object);
        SetUpBranchesForCleanup(git);
        git.Setup(g => g.DeleteBranchAsync(It.IsAny<string>(), "feature/done", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GitResult.Fail("error: cannot delete branch 'feature/done' checked out elsewhere\nmore"));
        await OpenAsync(dialog);

        dialog.BeginCleanup();
        await dialog.HandleKeyEventAsync(new KeyEvent('y', ConsoleKey.Y, false, false, false));
        await dialog.SyncTask;

        Assert.AreEqual("Deleted 0; 1 could not be deleted: error: cannot delete branch 'feature/done' checked out elsewhere", dialog.Status);
        Assert.AreEqual("feature/done", dialog.StatusDetail);
        notifications.Verify(n => n.Notify("Git sync", It.IsAny<string>(), NotificationSeverity.Error), Times.Once);
    }
}
