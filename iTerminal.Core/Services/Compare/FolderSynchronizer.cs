using iConsole.Data;

namespace iTerminal.Core.Services.Compare;

/// <summary>Which way a synchronisation moves files.</summary>
public enum SyncDirection
{
    /// <summary>Both ways: what only one side has goes to the other, and the newer copy replaces the older. A file with no newer side is left alone.</summary>
    Both,

    /// <summary>The left folder is the source: the right one gets what it lacks and what the left holds newer or different.</summary>
    LeftToRight,

    /// <summary>The right folder is the source.</summary>
    RightToLeft,
}

/// <param name="DeleteExtras">One-way only: what the target has and the source does not is removed (to the recycle bin where there is one). Ignored for <see cref="SyncDirection.Both"/>, where "missing on one side" cannot say which side is wrong.</param>
public sealed record SyncOptions(SyncDirection Direction = SyncDirection.Both, bool DeleteExtras = false);

public enum SyncActionKind
{
    CopyToRight,
    CopyToLeft,
    DeleteLeft,
    DeleteRight,
}

/// <summary>One step of a synchronisation: what is done to which item of the comparison.</summary>
public sealed record SyncAction(SyncActionKind Kind, DiffItem Item)
{
    public bool IsDelete => Kind is SyncActionKind.DeleteLeft or SyncActionKind.DeleteRight;

    public bool CopiesToRight => Kind == SyncActionKind.CopyToRight;

    /// <summary>The URL read from, for a copy.</summary>
    public Url? Source => Kind == SyncActionKind.CopyToRight ? Item.Left : Kind == SyncActionKind.CopyToLeft ? Item.Right : null;

    /// <summary>The URL removed, for a delete.</summary>
    public Url? Victim => Kind == SyncActionKind.DeleteLeft ? Item.Left : Kind == SyncActionKind.DeleteRight ? Item.Right : null;

    public long? SourceSize => Kind == SyncActionKind.CopyToRight ? Item.LeftSize : Item.RightSize;

    public DateTime? SourceTime => Kind == SyncActionKind.CopyToRight ? Item.LeftTime : Item.RightTime;

    public string Describe() => Kind switch
    {
        SyncActionKind.CopyToRight => $"copy ▶ {Item.Path}",
        SyncActionKind.CopyToLeft => $"◀ copy {Item.Path}",
        SyncActionKind.DeleteLeft => $"delete left {Item.Path}",
        _ => $"delete right {Item.Path}",
    };
}

/// <summary>What a synchronisation will do, worked out from a comparison and nothing else — no disk is touched.</summary>
/// <param name="Conflicts">Items with no winner (same date and different bytes, a file against a folder): left alone, and said so.</param>
/// <param name="Kept">Items the target holds newer than the source in a one-way run: left alone.</param>
public sealed record SyncPlan(IReadOnlyList<SyncAction> Actions, int Conflicts, int Kept)
{
    public int CopiesToRight => Actions.Count(a => a.Kind == SyncActionKind.CopyToRight);

    public int CopiesToLeft => Actions.Count(a => a.Kind == SyncActionKind.CopyToLeft);

    public int Deletes => Actions.Count(a => a.IsDelete);

    public bool IsEmpty => Actions.Count == 0;

    /// <summary>"3 copies to the right, 1 to the left, 2 deletions; 1 conflict left alone."</summary>
    public string Summary()
    {
        if (Actions.Count == 0)
        {
            return Conflicts == 0 ? "Nothing to synchronise." : $"Nothing to synchronise; {Conflicts} conflict{Plural(Conflicts)} left alone.";
        }

        var parts = new List<string>();
        if (CopiesToRight > 0)
        {
            parts.Add($"{CopiesToRight} cop{(CopiesToRight == 1 ? "y" : "ies")} to the right");
        }

        if (CopiesToLeft > 0)
        {
            parts.Add($"{CopiesToLeft} cop{(CopiesToLeft == 1 ? "y" : "ies")} to the left");
        }

        if (Deletes > 0)
        {
            parts.Add($"{Deletes} deletion{Plural(Deletes)}");
        }

        var text = string.Join(", ", parts) + ".";
        if (Conflicts > 0)
        {
            text += $" {Conflicts} conflict{Plural(Conflicts)} left alone.";
        }

        if (Kept > 0)
        {
            text += $" {Kept} newer on the target kept.";
        }

        return text;
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}

/// <summary>
/// Turns a <see cref="CompareResult"/> into the steps that make the two folders agree.
///
/// <para>The comparison is the whole decision: every difference already says which side is newer, so
/// the plan is a lookup. A folder present on both sides is never an action of its own — what differs
/// inside it is listed as items of its own (the comparison must include subfolders to see them) — and
/// a folder on one side only is one action that stands for everything under it.</para>
///
/// <para><b>Nothing is guessed.</b> A pair with no newer side, or a file against a folder, is a
/// conflict in a two-way run and is left alone. A one-way run is explicit about which side wins, so
/// there <see cref="DiffKind.Different"/> takes the source's copy — but a target file that is
/// <em>newer</em> than the source is kept, because overwriting work with an older copy is the one
/// thing a "sync" must not do silently.</para>
/// </summary>
public static class FolderSynchronizer
{
    public static SyncPlan Plan(CompareResult result, SyncOptions options)
    {
        var actions = new List<SyncAction>();
        var conflicts = 0;
        var kept = 0;
        var both = options.Direction == SyncDirection.Both;
        var toRight = options.Direction == SyncDirection.LeftToRight;
        var deleteExtras = options.DeleteExtras && !both;

        foreach (var item in result.Items)
        {
            if (item.Kind == DiffKind.Identical)
            {
                continue;
            }

            if (item.Left is not null && item.Right is not null)
            {
                if (item.IsFolder && IsFolder(item.Left) && IsFolder(item.Right))
                {
                    // On both sides: its verdict is only the sum of what is inside it.
                    continue;
                }

                if (IsFolder(item.Left) != IsFolder(item.Right))
                {
                    conflicts++;
                    continue;
                }
            }

            switch (item.Kind)
            {
                case DiffKind.LeftOnly:
                    if (both || toRight)
                    {
                        actions.Add(new SyncAction(SyncActionKind.CopyToRight, item));
                    }
                    else if (deleteExtras)
                    {
                        actions.Add(new SyncAction(SyncActionKind.DeleteLeft, item));
                    }

                    break;

                case DiffKind.RightOnly:
                    if (both || !toRight)
                    {
                        actions.Add(new SyncAction(SyncActionKind.CopyToLeft, item));
                    }
                    else if (deleteExtras)
                    {
                        actions.Add(new SyncAction(SyncActionKind.DeleteRight, item));
                    }

                    break;

                case DiffKind.LeftNewer:
                    if (both || toRight)
                    {
                        actions.Add(new SyncAction(SyncActionKind.CopyToRight, item));
                    }
                    else
                    {
                        kept++;
                    }

                    break;

                case DiffKind.RightNewer:
                    if (both || !toRight)
                    {
                        actions.Add(new SyncAction(SyncActionKind.CopyToLeft, item));
                    }
                    else
                    {
                        kept++;
                    }

                    break;

                case DiffKind.Different:
                    if (both)
                    {
                        conflicts++;
                    }
                    else
                    {
                        actions.Add(new SyncAction(toRight ? SyncActionKind.CopyToRight : SyncActionKind.CopyToLeft, item));
                    }

                    break;
            }
        }

        return new SyncPlan(actions, conflicts, kept);
    }

    private static bool IsFolder(Url url) => url.PathType == PathType.Folder;
}
