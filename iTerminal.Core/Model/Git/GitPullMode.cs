namespace iTerminal.Core.Model.Git;

/// <summary>How a pull reconciles the local commits with the upstream: the three choices <c>git pull</c> offers.</summary>
public enum GitPullMode
{
    /// <summary>Replays the local commits on top of the upstream (<c>pull --rebase</c>).</summary>
    Rebase,

    /// <summary>Merges the upstream in with a merge commit when needed (<c>pull --no-rebase</c>).</summary>
    Merge,

    /// <summary>Moves only when the upstream can be reached by a fast-forward; anything else fails (<c>pull --ff-only</c>).</summary>
    FastForwardOnly,
}
