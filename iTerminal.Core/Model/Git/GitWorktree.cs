namespace iTerminal.Core.Model.Git;

/// <summary>
/// One entry from <c>git worktree list --porcelain</c>. <see cref="Branch"/> is null for a detached HEAD.
/// The first entry git prints is the main working tree, which is never offered for removal.
/// </summary>
public sealed record GitWorktree(string Path, string? Branch, bool IsMain, bool IsLocked, bool IsPrunable);
