namespace iTerminal.Core.Model.Git;

/// <summary>
/// One entry from <c>git status --porcelain=v1</c>. <see cref="Code"/> is the two-character
/// XY status (X = index/staged, Y = working tree/unstaged); "??" marks an untracked file.
/// </summary>
public sealed record GitFileStatus(string Path, string Code, bool IsUntracked)
{
    public char IndexStatus => Code.Length > 0 ? Code[0] : ' ';
    public char WorkTreeStatus => Code.Length > 1 ? Code[1] : ' ';

    /// <summary>
    /// True for an unmerged path. Porcelain spells a conflict with a <c>U</c> on either side, or with
    /// the same letter on both (<c>DD</c>/<c>AA</c>) — a set worth naming, because a conflicted file is
    /// not "staged" in the sense the staging panel means, however much its index side has changed.
    /// </summary>
    public bool IsConflicted => !IsUntracked && Code.Length == 2 && Code switch
    {
        "DD" or "AA" or "AU" or "UA" or "DU" or "UD" or "UU" => true,
        _ => false,
    };

    /// <summary>True when the index (staged) side has a change. A conflict is never one — it belongs to the working tree until it is resolved.</summary>
    public bool HasStagedChange => !IsUntracked && !IsConflicted && IndexStatus is not ' ' and not '?';

    /// <summary>True when the working tree (unstaged) side has a change, including untracked and conflicted files.</summary>
    public bool HasWorkTreeChange => IsUntracked || IsConflicted || WorkTreeStatus is not ' ' and not '?';

    /// <summary>Short, human-friendly label for the change kind shown in the panels.</summary>
    public string Describe(bool staged)
    {
        if (IsUntracked)
        {
            return "?";
        }

        if (IsConflicted)
        {
            return "!";
        }

        var status = staged ? IndexStatus : WorkTreeStatus;
        return status switch
        {
            'M' => "M",
            'A' => "A",
            'D' => "D",
            'R' => "R",
            'C' => "C",
            'U' => "U",
            _ => status == ' ' ? " " : status.ToString(),
        };
    }
}
