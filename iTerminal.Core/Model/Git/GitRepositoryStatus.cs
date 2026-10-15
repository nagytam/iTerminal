namespace iTerminal.Core.Model.Git;

/// <summary>
/// Snapshot of a repository's working state. <see cref="Staged"/> and <see cref="Unstaged"/>
/// can overlap (a file with both staged and unstaged changes appears in both).
/// </summary>
public sealed record GitRepositoryStatus(
    string Branch,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<GitFileStatus> Staged,
    IReadOnlyList<GitFileStatus> Unstaged)
{
    public static GitRepositoryStatus Empty { get; } =
        new(string.Empty, null, 0, 0, [], []);
}
