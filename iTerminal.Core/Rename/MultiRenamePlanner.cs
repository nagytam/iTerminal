namespace iTerminal.Core.Rename;

/// <summary>Why a row of a multi-rename is, or is not, carried out.</summary>
public enum RenameStatus
{
    /// <summary>It will be renamed.</summary>
    Ok,

    /// <summary>The new name is the old one.</summary>
    Unchanged,

    /// <summary>The new name is empty or only blanks.</summary>
    Empty,

    /// <summary>The new name holds a separator, an invalid character, or is "." or "..".</summary>
    InvalidName,

    /// <summary>The new name is the old name of another entry in the batch, so the order would decide the result.</summary>
    Blocked,

    /// <summary>Another row of the batch gets the same new name.</summary>
    Duplicate,

    /// <summary>The new name is taken by an entry in the folder that is not part of the batch.</summary>
    Exists,
}

/// <summary>One entry of a multi-rename: its position in the batch, its names and what becomes of it.</summary>
public sealed record RenameRow(int Index, string OldName, string NewName, RenameStatus Status);

/// <summary>
/// Decides what each rename of a batch will do, from names alone: no disk, no listing beyond the
/// names already in memory, so it can run on every keystroke.
///
/// <para>The first rule that matches wins, in this order: Empty, InvalidName, Unchanged, Blocked,
/// Duplicate, Exists, Ok. A case-only change (<c>a</c> to <c>A</c>) is Ok: the row's own old name
/// is neither a blocker nor a sibling for itself. Duplicate and Blocked compare names without
/// case on every platform, so the result does not depend on the volume.</para>
/// </summary>
public static class MultiRenamePlanner
{
    private static readonly char[] SeparatorsAndInvalid =
        [.. System.IO.Path.GetInvalidFileNameChars(), '/', '\\'];

    /// <param name="entries">Each batch entry's current name and the name the pattern gives it, in batch order.</param>
    /// <param name="folderNames">Every name in the folder now, including the batch's own entries.</param>
    public static IReadOnlyList<RenameRow> Plan(IReadOnlyList<(string OldName, string NewName)> entries, IEnumerable<string> folderNames)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(folderNames);

        var batchNames = new HashSet<string>(entries.Select(e => e.OldName), StringComparer.OrdinalIgnoreCase);
        var siblings = new HashSet<string>(folderNames.Where(name => !batchNames.Contains(name)), StringComparer.OrdinalIgnoreCase);
        var oldNameRows = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var newNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Count; index++)
        {
            AddTo(oldNameRows, entries[index].OldName, index);
            newNameCounts[entries[index].NewName] = newNameCounts.GetValueOrDefault(entries[index].NewName) + 1;
        }

        var rows = new List<RenameRow>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            var (oldName, newName) = entries[index];
            rows.Add(new RenameRow(index, oldName, newName, StatusOf(index, oldName, newName, oldNameRows, newNameCounts, siblings)));
        }

        return rows;
    }

    private static RenameStatus StatusOf(
        int index,
        string oldName,
        string newName,
        Dictionary<string, List<int>> oldNameRows,
        Dictionary<string, int> newNameCounts,
        HashSet<string> siblings)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return RenameStatus.Empty;
        }

        if (newName.IndexOfAny(SeparatorsAndInvalid) >= 0 || newName is "." or "..")
        {
            return RenameStatus.InvalidName;
        }

        if (string.Equals(newName, oldName, StringComparison.Ordinal))
        {
            return RenameStatus.Unchanged;
        }

        if (oldNameRows.TryGetValue(newName, out var holders) && holders.Any(other => other != index))
        {
            return RenameStatus.Blocked;
        }

        if (newNameCounts.GetValueOrDefault(newName) > 1)
        {
            return RenameStatus.Duplicate;
        }

        return siblings.Contains(newName) ? RenameStatus.Exists : RenameStatus.Ok;
    }

    private static void AddTo(Dictionary<string, List<int>> map, string key, int index)
    {
        if (!map.TryGetValue(key, out var list))
        {
            map[key] = list = [];
        }

        list.Add(index);
    }
}
