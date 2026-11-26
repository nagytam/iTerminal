using iConsole.Controls;

namespace iTerminal.DevelopmentStudio.Model;

/// <summary>The panels of the Development studio. Each one can be docked to any edge or to the centre.</summary>
public enum StudioPanelKind
{
    /// <summary>The source view (the centre by default).</summary>
    Editor,

    /// <summary>The Solution, Test or File Explorer (the right edge by default).</summary>
    Explorer,

    /// <summary>The output panel: Output, Debug, Coverage and Profile (the bottom edge by default).</summary>
    Output,

    /// <summary>The Error List of the errors and warnings the commands printed (the top edge by default).</summary>
    Errors,
}

/// <summary>The result of <see cref="StudioPanelLayout.Move"/>.</summary>
public enum StudioPanelMove
{
    /// <summary>The panel went to an empty edge.</summary>
    Moved,

    /// <summary>The panel traded places with the one that was on the target side.</summary>
    Swapped,

    /// <summary>The panel is already on that side.</summary>
    Unchanged,

    /// <summary>The centre cannot move to an empty edge, because the centre must always hold a panel.</summary>
    Refused,
}

/// <summary>
/// Where each panel of the studio is docked. Five slots (the four edges and the centre) hold four panels, so one slot is
/// always empty, and the centre is never empty: a panel moved onto a taken slot swaps places with its occupant, and the
/// centre panel can only swap. An edge panel has a share of the studio's width or height (<see cref="Percent"/>), which
/// <see cref="Resize"/> changes in steps. This class holds the arrangement only; the dialog builds the controls from it.
/// </summary>
public sealed class StudioPanelLayout
{
    /// <summary>The smallest and largest share, in percent, an edge panel can be resized to.</summary>
    public const int MinPercent = 5;
    public const int MaxPercent = 80;

    /// <summary>The share an edge panel gets when it is moved out of the centre, which has no share of its own.</summary>
    private const int CentreExitPercent = 25;

    private readonly Dictionary<StudioPanelKind, (DockSide Side, int Percent)> _placements = new()
    {
        [StudioPanelKind.Editor] = (DockSide.Fill, 0),
        [StudioPanelKind.Explorer] = (DockSide.Right, 34),
        [StudioPanelKind.Output] = (DockSide.Bottom, 33),
        [StudioPanelKind.Errors] = (DockSide.Top, 15),
    };

    /// <summary>The arrangement the studio opens with: the editor in the centre, the Explorer on the right, the output at the bottom and the Error List on top.</summary>
    public static StudioPanelLayout Default() => new();

    /// <summary>The side a panel is docked to.</summary>
    public DockSide SideOf(StudioPanelKind kind) => _placements[kind].Side;

    /// <summary>The share of the width or height an edge panel takes, in percent. Meaningless for the centre panel.</summary>
    public int PercentOf(StudioPanelKind kind) => Share(_placements[kind].Percent);

    /// <summary>The share a stored percent stands for: the centre panel keeps 0, which an edge reads as the default share.</summary>
    private static int Share(int percent) => percent == 0 ? CentreExitPercent : percent;

    /// <summary>The panel docked to <paramref name="side"/>, or null when that edge is empty.</summary>
    public StudioPanelKind? At(DockSide side)
    {
        foreach ((StudioPanelKind kind, (DockSide placed, int _)) in _placements)
        {
            if (placed == side) return kind;
        }

        return null;
    }

    /// <summary>The panels in the order they are docked: top, bottom, left, right, and the centre last.</summary>
    public IReadOnlyList<(StudioPanelKind Kind, DockSide Side, int Percent)> Docked()
    {
        List<(StudioPanelKind, DockSide, int)> docked = [];
        foreach (DockSide side in new[] { DockSide.Top, DockSide.Bottom, DockSide.Left, DockSide.Right, DockSide.Fill })
        {
            if (At(side) is { } kind) docked.Add((kind, side, Share(_placements[kind].Percent)));
        }

        return docked;
    }

    /// <summary>
    /// Docks <paramref name="kind"/> to <paramref name="side"/>. A taken side is swapped with the panel on it; an empty edge
    /// takes the panel, unless the panel is the centre one, which would leave the centre empty.
    /// </summary>
    public StudioPanelMove Move(StudioPanelKind kind, DockSide side)
    {
        (DockSide from, int percent) = _placements[kind];
        if (from == side) return StudioPanelMove.Unchanged;

        if (At(side) is { } occupant)
        {
            _placements[occupant] = (from, _placements[occupant].Percent);
            _placements[kind] = (side, percent);
            return StudioPanelMove.Swapped;
        }

        if (from == DockSide.Fill) return StudioPanelMove.Refused;

        _placements[kind] = (side, percent);
        return StudioPanelMove.Moved;
    }

    /// <summary>
    /// Changes an edge panel's share by <paramref name="delta"/> percent, within <see cref="MinPercent"/> and
    /// <see cref="MaxPercent"/>. Returns false for the centre panel, which fills what the edges leave, or when the share does not change.
    /// </summary>
    public bool Resize(StudioPanelKind kind, int delta)
    {
        (DockSide side, int percent) = _placements[kind];
        if (side == DockSide.Fill) return false;

        int resized = Math.Clamp(Share(percent) + delta, MinPercent, MaxPercent);
        if (resized == Share(percent)) return false;

        _placements[kind] = (side, resized);
        return true;
    }
}
