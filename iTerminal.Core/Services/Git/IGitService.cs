using iTerminal.Core.Model.Git;

namespace iTerminal.Core.Services.Git;

/// <summary>
/// Async, structured access to a git working tree. Every member runs git off the UI threads;
/// callers must never await these from a render/draw path.
/// </summary>
public interface IGitService
{
    /// <summary>Returns the repository root for <paramref name="workingDirectory"/>, or null if it is not inside a repo.</summary>
    Task<string?> GetRepositoryRootAsync(string workingDirectory, CancellationToken cancellationToken = default);

    Task<GitRepositoryStatus> GetStatusAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitCommit>> GetLogAsync(string repositoryRoot, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>Diff for a single file. <paramref name="staged"/> selects <c>--cached</c>.</summary>
    Task<string> GetDiffAsync(string repositoryRoot, string relativePath, bool staged, bool isUntracked, CancellationToken cancellationToken = default);

    /// <summary>Full patch for a commit (<c>git show</c>).</summary>
    Task<string> GetCommitDiffAsync(string repositoryRoot, string commit, CancellationToken cancellationToken = default);

    Task<GitResult> StageAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Stages every change in the working tree, including untracked files (<c>git add -A</c>).</summary>
    Task<GitResult> StageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    Task<GitResult> UnstageAsync(string repositoryRoot, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>Empties the index back to HEAD without touching the working tree.</summary>
    Task<GitResult> UnstageAllAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    /// <summary>Discards working-tree changes for a tracked file, or deletes an untracked file.</summary>
    Task<GitResult> DiscardAsync(string repositoryRoot, string relativePath, bool isUntracked, CancellationToken cancellationToken = default);

    Task<GitResult> CommitAsync(string repositoryRoot, string message, bool amend, CancellationToken cancellationToken = default);

    /// <summary>Subject and body of HEAD, or an empty string in a repository without commits. Used to pre-fill an amend.</summary>
    Task<string> GetLastCommitMessageAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    /// <summary>Local branches, plus the remote-tracking branches when <paramref name="includeRemotes"/> is set.</summary>
    Task<IReadOnlyList<GitBranch>> GetBranchesAsync(string repositoryRoot, bool includeRemotes = false, CancellationToken cancellationToken = default);

    Task<GitResult> CheckoutBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a local branch tracking <paramref name="remoteBranchName"/> (e.g. <c>origin/feature</c>)
    /// and checks it out. Falls back to a plain checkout when the local branch already exists.
    /// </summary>
    Task<GitResult> CheckoutRemoteBranchAsync(string repositoryRoot, string remoteBranchName, CancellationToken cancellationToken = default);

    /// <summary>Creates a new branch from HEAD and checks it out.</summary>
    Task<GitResult> CreateBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default);

    /// <summary>Merges <paramref name="branchName"/> into the current branch (<c>--no-edit</c>).</summary>
    Task<GitResult> MergeBranchAsync(string repositoryRoot, string branchName, CancellationToken cancellationToken = default);

    Task<GitResult> DeleteBranchAsync(string repositoryRoot, string branchName, bool force, CancellationToken cancellationToken = default);

    /// <summary>Aborts an in-progress merge, restoring the pre-merge state.</summary>
    Task<GitResult> AbortMergeAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    /// <summary>True when a merge is in progress (MERGE_HEAD exists) — e.g. after a conflicting merge.</summary>
    Task<bool> IsMergeInProgressAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    Task<GitResult> FetchAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    Task<GitResult> PullAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes the current branch. With <paramref name="setUpstream"/> the branch is published to the
    /// default remote and made to track it — what a plain <c>git push</c> refuses to do for a branch
    /// that has no upstream yet.
    /// </summary>
    Task<GitResult> PushAsync(string repositoryRoot, bool setUpstream = false, CancellationToken cancellationToken = default);

    /// <summary>The remote a push would publish to — <c>origin</c> when it exists, else the first one, or null when the repository has no remotes.</summary>
    Task<string?> GetDefaultRemoteAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitStash>> GetStashesAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    /// <summary>Stashes the working tree including untracked files, under <paramref name="message"/> when one is given.</summary>
    Task<GitResult> StashPushAsync(string repositoryRoot, string? message = null, CancellationToken cancellationToken = default);

    Task<GitResult> StashPopAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default);

    Task<GitResult> StashApplyAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default);

    Task<GitResult> StashDropAsync(string repositoryRoot, int index, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a mutating git command.</summary>
public sealed record GitResult(bool Success, string Output)
{
    public static GitResult Ok(string output = "") => new(true, output);
    public static GitResult Fail(string output) => new(false, output);
}
