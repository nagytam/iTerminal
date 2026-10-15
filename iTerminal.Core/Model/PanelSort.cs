using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Model;

public enum PanelSortMode
{
    /// <summary>The order the drive lists in: folders then files by name on a disk, newest first in the recycle bin, history order on the git drive.</summary>
    AsListed,
    Name,
    Extension,
    Size,
    Date,
}

/// <summary>How a panel orders its rows: by what, and whether backwards.</summary>
public readonly record struct PanelSort(PanelSortMode Mode, bool Descending = false)
{
    public static PanelSort AsListed { get; } = new(PanelSortMode.AsListed);

    public bool IsAsListed => Mode == PanelSortMode.AsListed;

    public static string DisplayName(PanelSortMode mode) => mode switch
    {
        PanelSortMode.Name => "Name",
        PanelSortMode.Extension => "Extension",
        PanelSortMode.Size => "Size",
        PanelSortMode.Date => "Date",
        _ => "As listed",
    };

    /// <summary>For a panel's footer: "size ↓", "name ↑" — empty for the drive's own order, which needs no saying.</summary>
    public string Describe() =>
        IsAsListed ? string.Empty : $"{DisplayName(Mode).ToLowerInvariant()} {(Descending ? "↓" : "↑")}";

    /// <summary>The form kept in the settings: <c>Size,desc</c>.</summary>
    public string Serialize() => $"{Mode}{(Descending ? ",desc" : string.Empty)}";

    /// <summary>Reads <see cref="Serialize"/>'s form; anything else is the drive's own order.</summary>
    public static PanelSort Parse(string? text)
    {
        var parts = (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Enum.TryParse<PanelSortMode>(parts[0], ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
        {
            return AsListed;
        }

        return new PanelSort(mode, parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The settings key a panel's sort is kept under.</summary>
    public static string SettingsKey(string panelName) => panelName + ".Sort";

    /// <summary>
    /// <paramref name="entries"/> in this order. Three things hold whatever the mode:
    /// the <c>..</c> row stays first, since it is the way out and not an entry; folders stay ahead
    /// of files, in both directions — "largest first" should not bury the folders under the files;
    /// and rows that tie are in name order, ascending, so a sort by extension or by date reads as
    /// a list rather than a shuffle.
    /// </summary>
    public IList<IEntry> Apply(IList<IEntry> entries) => Apply(entries, PanelViewOptions.Default);

    /// <summary>
    /// As <see cref="Apply(IList{IEntry})"/>, with the settings page's say: hidden entries left out
    /// (never the <c>..</c> row — the parent of a folder inside a hidden one is hidden too, and is
    /// still the way out), folders mixed in with the files, names compared digit by digit.
    /// "As listed" is still the drive's own order: only the hidden entries are taken out of it.
    /// </summary>
    public IList<IEntry> Apply(IList<IEntry> entries, PanelViewOptions options)
    {
        if (!options.ShowHidden && entries.Any(e => e.Url.IsHidden && !FocusedEntry.IsParentEntry(e)))
        {
            entries = [.. entries.Where(e => !e.Url.IsHidden || FocusedEntry.IsParentEntry(e))];
        }

        if (IsAsListed || entries.Count < 2)
        {
            return entries;
        }

        var sorted = new List<IEntry>(entries.Count);
        var rest = new List<IEntry>(entries.Count);
        foreach (var entry in entries)
        {
            (FocusedEntry.IsParentEntry(entry) ? sorted : rest).Add(entry);
        }

        var mode = Mode;
        var direction = Descending ? -1 : 1;
        Comparison<IEntry> CompareNames = options.NaturalOrder
            ? (x, y) => NaturalCompare(x.DisplayName, y.DisplayName)
            : (x, y) => string.Compare(x.DisplayName, y.DisplayName, StringComparison.OrdinalIgnoreCase);
        rest.Sort((x, y) =>
        {
            var folders = options.FoldersFirst ? IsFolder(y).CompareTo(IsFolder(x)) : 0;
            if (folders != 0)
            {
                return folders;
            }

            var byKey = mode switch
            {
                PanelSortMode.Extension => IsFolder(x) ? 0 : string.Compare(x.Url.Extension, y.Url.Extension, StringComparison.OrdinalIgnoreCase),
                PanelSortMode.Size => IsFolder(x) ? 0 : SizeOf(x).CompareTo(SizeOf(y)),
                PanelSortMode.Date => string.CompareOrdinal(x.Date + x.Time, y.Date + y.Time),
                _ => CompareNames(x, y),
            };

            return byKey != 0 ? byKey * direction : CompareNames(x, y) * (mode == PanelSortMode.Name ? direction : 1);
        });

        sorted.AddRange(rest);
        return sorted;
    }

    private static bool IsFolder(IEntry entry) => entry.Url.PathType == PathType.Folder;

    /// <summary>
    /// Compares names the way a person reads them: letters without regard to case, and a run of
    /// digits as the number it is — so <c>file2</c> comes before <c>file10</c>, where character by
    /// character the 1 would put it first. Numbers of any length: the runs are compared by their
    /// length once leading zeros are off, then digit by digit, never parsed.
    /// </summary>
    public static int NaturalCompare(string? x, string? y)
    {
        x ??= string.Empty;
        y ??= string.Empty;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var digitsX = x.AsSpan(startX, i - startX).TrimStart('0');
                var digitsY = y.AsSpan(startY, j - startY).TrimStart('0');
                var byNumber = digitsX.Length != digitsY.Length
                    ? digitsX.Length.CompareTo(digitsY.Length)
                    : digitsX.SequenceCompareTo(digitsY);
                if (byNumber != 0)
                {
                    return byNumber;
                }

                continue;
            }

            var byLetter = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (byLetter != 0)
            {
                return byLetter;
            }

            i++;
            j++;
        }

        var byLength = (x.Length - i).CompareTo(y.Length - j);

        // "file01" and "file1" are the same number: an order between them, so the sort is stable.
        return byLength != 0 ? byLength : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A tool-backed drive's rows carry their size themselves; a local file's is in its URL.</summary>
    private static long SizeOf(IEntry entry) =>
        entry is VirtualDriveEntry { Size: > 0 } listed ? listed.Size : entry.Url.Length;
}

/// <summary>
/// What the settings page says about every panel's listing, whatever its sort: whether hidden
/// entries are listed, whether folders are kept ahead of files, and how names are compared.
/// The defaults are what the panels did before there was a page.
/// </summary>
public readonly record struct PanelViewOptions(bool ShowHidden = true, bool FoldersFirst = true, bool NaturalOrder = true)
{
    public const string ShowHiddenKey = "Panels.ShowHidden";
    public const string FoldersFirstKey = "Panels.FoldersFirst";
    public const string NaturalOrderKey = "Panels.NaturalOrder";

    public static PanelViewOptions Default { get; } = new(true, true, true);

    public static PanelViewOptions Load(Services.ISettingsService? settings) => settings is null
        ? Default
        : new PanelViewOptions(
            settings.GetAsBool(ShowHiddenKey, true) ?? true,
            settings.GetAsBool(FoldersFirstKey, true) ?? true,
            settings.GetAsBool(NaturalOrderKey, true) ?? true);

    public void Save(Services.ISettingsService settings)
    {
        settings.Set(ShowHiddenKey, ShowHidden.ToString());
        settings.Set(FoldersFirstKey, FoldersFirst.ToString());
        settings.Set(NaturalOrderKey, NaturalOrder.ToString());
    }
}

/// <summary>A panel whose rows can be put in another order.</summary>
public interface ISortableContentModel
{
    PanelViewOptions ViewOptions { get; }

    /// <summary>Applies the settings page's options, keeping the cursor on the entry it was on.</summary>
    void SetViewOptions(PanelViewOptions options);

    /// <summary>False where the order is the content: a playing queue, a preview.</summary>
    bool CanSort { get; }

    PanelSort Sort { get; }

    /// <summary>Re-orders the rows, keeping the cursor on the entry it was on.</summary>
    void SetSort(PanelSort sort);
}
