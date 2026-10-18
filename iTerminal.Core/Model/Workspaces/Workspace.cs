namespace iTerminal.Core.Model.Workspaces;

/// <summary>What fills the bottom of the screen under the two panels.</summary>
public enum WorkspaceBar
{
    /// <summary>The AI prompt bar (the default).</summary>
    Ai,

    /// <summary>The command line (terminal bar).</summary>
    Terminal,

    /// <summary>The full-screen terminal, which hides both panels.</summary>
    FullTerminal,
}

/// <summary>
/// The screen as a person arranged it: where the two panels are and how they are ordered, what is under
/// them, and which panel has the cursor. Nothing here is a credential — a remote location is only its URL.
/// </summary>
public sealed record WorkspaceLayout(
    string LeftUrl,
    string RightUrl,
    string LeftSort,
    string RightSort,
    WorkspaceBar Bar,
    string FocusedPanel)
{
    /// <summary>
    /// Whether two layouts put the same things on screen. The focused panel is left out on purpose: the
    /// cursor moves all the time, and a workspace should not read as "changed" every time it does.
    /// </summary>
    public bool SameScreenAs(WorkspaceLayout other) =>
        string.Equals(LeftUrl, other.LeftUrl, StringComparison.OrdinalIgnoreCase)
        && string.Equals(RightUrl, other.RightUrl, StringComparison.OrdinalIgnoreCase)
        && string.Equals(LeftSort, other.LeftSort, StringComparison.OrdinalIgnoreCase)
        && string.Equals(RightSort, other.RightSort, StringComparison.OrdinalIgnoreCase)
        && Bar == other.Bar;
}

/// <summary>A named, saved <see cref="WorkspaceLayout"/>.</summary>
public sealed record Workspace(string Name, WorkspaceLayout Layout, DateTime SavedUtc);
