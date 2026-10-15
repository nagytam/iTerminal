using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Lenses;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Model.Git;
using iTerminal.Core.Services.Git;
using Moq;

namespace iTerminal.Core.Test.Controls.Lenses;

[TestClass]
public class GitLensTest
{
    /// <summary>Minimal <see cref="IGitService"/> fake — GitLens only calls the two read methods.</summary>
    private sealed class FakeGitService : IGitService
    {
        public string? Root { get; set; }
        public GitRepositoryStatus Status { get; set; } = GitRepositoryStatus.Empty;

        public Task<string?> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken = default)
            => Task.FromResult(Root);

        public Task<GitRepositoryStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default)
            => Task.FromResult(Status);

        public Task<IReadOnlyList<GitCommit>> GetLogAsync(string repositoryRoot, int maxCount, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<GitCommit>>([]);

        public Task<string> GetDiffAsync(string repositoryRoot, string relativePath, bool staged, bool isUntracked, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<string> GetCommitDiffAsync(string repositoryRoot, string commit, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public Task<GitResult> StageAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> StageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> UnstageAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> UnstageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> DiscardAsync(string repositoryRoot, string relativePath, bool isUntracked, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> CommitAsync(string repositoryRoot, string message, bool amend, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<string> GetLastCommitMessageAsync(string repositoryRoot, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repositoryRoot, bool includeRemotes = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitBranch>>([]);
        public Task<GitResult> CheckoutBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> CheckoutRemoteBranchAsync(string repositoryRoot, string remoteBranchName, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> MergeBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> DeleteBranchAsync(string repositoryRoot, string branchName, bool force, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> AbortMergeAsync(string repositoryRoot, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<bool> IsMergeInProgressAsync(string repositoryRoot, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<GitResult> FetchAsync(string repositoryRoot, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> PullAsync(string repositoryRoot, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> PushAsync(string repositoryRoot, bool setUpstream = false, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<string?> GetDefaultRemoteAsync(string repositoryRoot, CancellationToken cancellationToken = default) => Task.FromResult<string?>("origin");
        public Task<IReadOnlyList<GitStash>> GetStashesAsync(string repositoryRoot, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitStash>>([]);
        public Task<GitResult> StashPushAsync(string repositoryRoot, string? message = null, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> StashPopAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> StashApplyAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
        public Task<GitResult> StashDropAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default) => Task.FromResult(GitResult.Ok());
    }

    private static T PollUntil<T>(Func<T> action, Func<T, bool> isSatisfied, int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        T result = action();
        while (!isSatisfied(result) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
            result = action();
        }

        return result;
    }

    [TestMethod]
    public void GetHeaderIndicator_WhenFolderIsGitRepository_ReturnsGitIndicator()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            var gitService = new FakeGitService { Root = rootPath };

            var lens = new GitLens(gitService);

            var contentModel = new Mock<IContentModel>(MockBehavior.Strict);
            contentModel.SetupGet(m => m.Url).Returns(new Url(rootPath));

            // Git runs off the render thread, so the indicator is populated by a background
            // refresh; poll until it becomes available.
            var indicator = PollUntil(() => lens.GetHeaderIndicator(contentModel.Object), result => result == "{Git}");

            Assert.AreEqual("{Git}", indicator);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }
        }
    }

    [TestMethod]
    public void DecorateRow_WhenFileIsModified_AppliesBackgroundHighlight()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        var filePath = Path.Combine(rootPath, "modified.txt");
        File.WriteAllText(filePath, "test");

        try
        {
            var gitService = new FakeGitService
            {
                Root = rootPath,
                Status = new GitRepositoryStatus(
                    "main", null, 0, 0,
                    Staged: [],
                    Unstaged: [new GitFileStatus("modified.txt", " M", IsUntracked: false)]),
            };

            var lens = new GitLens(gitService);

            var contentModel = new Mock<IContentModel>(MockBehavior.Strict);
            contentModel.SetupGet(m => m.Url).Returns(new Url(rootPath));

            // Drive the background refresh and wait until the repository state (including the
            // per-file status) is published before asserting on the decoration.
            PollUntil(() => lens.GetHeaderIndicator(contentModel.Object), result => result == "{Git}");

            var entry = new FileSystemEntry(filePath);
            var row = entry.FormatItemName(40, false, new ColorScheme(), VisualState.Normal);

            lens.DecorateRow(contentModel.Object, entry, row, false, new ColorScheme(), VisualState.Normal);

            Assert.IsTrue(row.String.Any(conxel => conxel.BackgroundColor == Color.DarkBlue));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, true);
            }
        }
    }
}
