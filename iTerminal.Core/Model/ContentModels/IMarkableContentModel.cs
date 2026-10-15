using iConsole.Interfaces;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A panel whose rows can be marked, so F5, Shift+F6 and F8 act on several entries at once instead
/// of the one under the cursor.
///
/// <para>The marks belong to the folder they were made in. Navigating anywhere else drops them —
/// an operation must never act on rows the user can no longer see — and a marked entry that has
/// left the listing (deleted, renamed) simply stops being reported.</para>
/// </summary>
public interface IMarkableContentModel
{
    /// <summary>False for the drives whose rows are not files (the music drives, the preview).</summary>
    bool CanMark { get; }

    /// <summary>How many of the listed entries are marked.</summary>
    int MarkedCount { get; }

    bool IsMarked(IEntry? entry);

    /// <summary>Marks or unmarks <paramref name="entry"/>. The <c>..</c> row is never marked; returns whether anything changed.</summary>
    bool SetMark(IEntry? entry, bool marked);

    /// <summary>Flips <paramref name="entry"/>'s mark; returns whether anything changed.</summary>
    bool ToggleMark(IEntry? entry);

    /// <summary>
    /// Marks (or unmarks) every listed entry whose name matches one of <paramref name="patterns"/>
    /// — the same <c>*.cs;*.md</c> syntax F7 takes. Folders are left alone unless
    /// <paramref name="includeFolders"/>. Returns how many marks changed.
    /// </summary>
    int SetMarks(string patterns, bool marked, bool includeFolders);

    /// <summary>Flips every listed file's mark; folders too when <paramref name="includeFolders"/>.</summary>
    void InvertMarks(bool includeFolders);

    void ClearMarks();

    /// <summary>The marked entries, in listing order.</summary>
    IReadOnlyList<IEntry> GetMarkedEntries();
}
