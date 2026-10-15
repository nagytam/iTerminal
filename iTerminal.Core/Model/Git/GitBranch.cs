namespace iTerminal.Core.Model.Git;

/// <summary>
/// A branch from <c>git for-each-ref</c>. Remote-tracking branches (<c>refs/remotes</c>) are listed
/// alongside the local ones with <see cref="IsRemote"/> set — checking one out means creating a
/// local branch that tracks it, which is a different command and so has to be a distinguishable row.
/// </summary>
public sealed record GitBranch(string Name, bool IsCurrent, string? Upstream, bool IsRemote = false);
