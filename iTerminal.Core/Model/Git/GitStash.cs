namespace iTerminal.Core.Model.Git;

/// <summary>An entry from <c>git stash list</c>. <see cref="Index"/> maps to <c>stash@{Index}</c>.</summary>
public sealed record GitStash(int Index, string Message);
