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

    /// <summary>The new name holds a separator or an invalid character, is "." or "..", or (under Windows rules) ends in a dot or a blank or is a reserved device name.</summary>
    InvalidName,

    /// <summary>The new name differs from the old one only in case. The engine would see the file itself as the existing target on a case-insensitive volume, so it is not run, on any platform.</summary>
    CaseOnly,

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
/// <para>The first rule that matches wins, in this order: Empty, InvalidName, Unchanged, CaseOnly,
/// Blocked, Duplicate, Exists, Ok. A case-only change (<c>a</c> to <c>A</c>) is CaseOnly and is not
/// run. Blocked, Duplicate and Exists compare names without case on every platform, so the result
/// does not depend on the volume. Siblings are excluded by exact name, so a folder's <c>foo.txt</c>
/// is still seen next to a batch entry <c>Foo.txt</c>.</para>
/// </summary>
public static class MultiRenamePlanner
{
    private static readonly char[] SeparatorsAndInvalid =
        [.. System.IO.Path.GetInvalidFileNameChars(), '/', '\\'];

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <param name="entries">Each batch entry's current name and the name the pattern gives it, in batch order.</param>
    /// <param name="folderNames">Every name in the folder now, including the batch's own entries.</param>
    /// <param name="windowsNameRules">Whether Windows's file-name rules apply to the new names (the dialog passes <c>OperatingSystem.IsWindows()</c>).</param>
    public static IReadOnlyList<RenameRow> Plan(
        IReadOnlyList<(string OldName, string NewName)> entries,
        IEnumerable<string> folderNames,
        bool windowsNameRules = false)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(folderNames);

        // Excluded by exact name: on a case-sensitive folder, a sibling foo.txt is a different file from the batch's Foo.txt.
        var batchExactNames = new HashSet<string>(entries.Select(e => e.OldName), StringComparer.Ordinal);
        var siblings = new HashSet<string>(folderNames.Where(name => !batchExactNames.Contains(name)), StringComparer.OrdinalIgnoreCase);
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
            rows.Add(new RenameRow(index, oldName, newName, StatusOf(index, oldName, newName, oldNameRows, newNameCounts, siblings, windowsNameRules)));
        }

        return rows;
    }

    private static RenameStatus StatusOf(
        int index,
        string oldName,
        string newName,
        Dictionary<string, List<int>> oldNameRows,
        Dictionary<string, int> newNameCounts,
        HashSet<string> siblings,
        bool windowsNameRules)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return RenameStatus.Empty;
        }

        if (newName.IndexOfAny(SeparatorsAndInvalid) >= 0 || newName is "." or ".." || (windowsNameRules && IsWindowsInvalid(newName)))
        {
            return RenameStatus.InvalidName;
        }

        if (string.Equals(newName, oldName, StringComparison.Ordinal))
        {
            return RenameStatus.Unchanged;
        }

        if (string.Equals(newName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            return RenameStatus.CaseOnly;
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

    /// <summary>Names Windows will not create: a trailing dot or blank, or a reserved device name with or without an extension (<c>CON.txt</c>).</summary>
    private static bool IsWindowsInvalid(string name)
    {
        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            return true;
        }

        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        return ReservedWindowsNames.Contains(stem);
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
