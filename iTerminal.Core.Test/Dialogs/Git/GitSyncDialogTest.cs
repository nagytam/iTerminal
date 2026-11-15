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

    private (GitSyncDialog Dialog, Mock<IGitService> Git) Create(string? upstream = "origin/main", IReadOnlyList<GitFileStatus>? unstaged = null)
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
}
