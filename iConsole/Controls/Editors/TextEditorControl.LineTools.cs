using System.Text;

namespace iConsole.Controls.Editors;

/// <summary>
/// The line tools: move, duplicate and comment lines, and the utilities that rewrite them (trim, join, remove duplicate or
/// blank lines, tabs to spaces). Each one works on the lines the selection touches, or on the cursor's line, or on the
/// whole file where the operation is about the file, and is one undo step.
/// </summary>
public partial class TextEditorControl
{
    /// <summary>
    /// Moves the selected lines, or the cursor's line, one line up or down past its neighbour. Returns whether anything
    /// moved: nothing does at the first or last line.
    /// </summary>
    public bool MoveLines(bool down)
    {
        if (IsReadOnly)
        {
            return false;
        }

        var count = EnsureLines();
        var (first, last) = LineRangeAtCursor();
        if (down ? last >= count - 1 : first <= 0)
        {
            return false;
        }

        var step = down ? 1 : -1;
        var hadSelection = HasSelection;
        // Down: the line under the block goes on top of it. Up: the line above the block goes under it.
        return ReplaceRows(
            down ? first : first - 1,
            down ? last + 1 : last,
            segment => down ? [segment[^1], .. segment[..^1]] : [.. segment[1..], segment[0]],
            cursorDelta: step,
            selectRows: _ => hadSelection ? (first + step, last + step) : null);
    }

    /// <summary>
    /// Copies the selected lines, or the cursor's line, to the line below them. The cursor goes down with the copy; with a
    /// selection the copy is the one selected. Returns whether a line was added.
    /// </summary>
    public bool DuplicateLines()
    {
        if (IsReadOnly)
        {
            return false;
        }

        EnsureLines();
        var (first, last) = LineRangeAtCursor();
        var size = last - first + 1;
        var hadSelection = HasSelection;
        return ReplaceRows(
            first,
            last,
            segment => [.. segment, .. segment],
            cursorDelta: size,
            selectRows: _ => hadSelection ? (last + 1, last + size) : null);
    }

    /// <summary>
    /// Adds the first line-comment marker of the language (<c>//</c>, <c>#</c>, <c>--</c> and so on) to the lines at their
    /// common indent, or removes it when every non-blank line already has it. Blank lines are left alone. Returns whether
    /// anything changed: nothing for a language without a line comment.
    /// </summary>
    public bool ToggleLineComment()
    {
        if (IsReadOnly || Syntax?.LineComments is not { Length: > 0 } markers)
        {
            return false;
        }

        var marker = markers[0];
        EnsureLines();
        var (first, last) = LineRangeAtCursor();
        var lines = TextModel.Lines;
        var rows = Enumerable.Range(first, last - first + 1).Where(row => lines[row].Trim().Length > 0).ToArray();
        if (rows.Length == 0)
        {
            return false;
        }

        var commented = rows.All(row => lines[row].TrimStart().StartsWith(marker, StringComparison.Ordinal));
        var indent = rows.Min(row => lines[row].Length - lines[row].TrimStart().Length);
        var hadSelection = HasSelection;
        return ReplaceRows(
            first,
            last,
            segment => segment.Select(line => ToggledComment(line, marker, indent, commented)).ToArray(),
            cursorDelta: 0,
            selectRows: _ => hadSelection ? (first, last) : null);
    }

    /// <summary>Removes the spaces and tabs at the end of each line of the selection, or of the whole file with nothing selected.</summary>
    public bool TrimTrailingWhitespace() => TransformLines(segment => segment.Select(line => line.TrimEnd(' ', '\t')).ToArray());

    /// <summary>Removes every line that is blank (or only spaces and tabs), in the selection or in the whole file.</summary>
    public bool RemoveBlankLines() => TransformLines(segment => segment.Where(line => line.Trim().Length > 0).ToArray());

    /// <summary>
    /// Removes each line that repeats an earlier one, keeping the first of each, in the selection or in the whole file.
    /// The comparison is case-sensitive.
    /// </summary>
    public bool RemoveDuplicateLines() => TransformLines(segment => segment.Distinct(StringComparer.Ordinal).ToArray());

    /// <summary>
    /// Turns every tab into spaces up to the next multiple of <see cref="IndentWidth"/>, in the selection or in the whole
    /// file, so the text lines up the same in any editor.
    /// </summary>
    public bool TabsToSpaces() => TransformLines(segment => segment.Select(ExpandTabs).ToArray());

    /// <summary>
    /// Joins the selected lines into one, with a single space where each line met the next and their surrounding spaces
    /// trimmed; with nothing selected, the cursor's line is joined to the one below. Returns whether a line was removed.
    /// </summary>
    public bool JoinLines()
    {
        if (IsReadOnly)
        {
            return false;
        }

        var count = EnsureLines();
        var (first, last) = LineRangeAtCursor();
        if (last == first)
        {
            if (first >= count - 1)
            {
                return false;
            }

            last = first + 1;
        }

        return ReplaceRows(
            first,
            last,
            segment => [JoinSegment(segment)],
            cursorDelta: 0,
            selectRows: _ => null);
    }

    // ---- Helpers ----

    /// <summary>Makes sure there is a line to work on, and returns how many there are.</summary>
    private long EnsureLines()
    {
        var lines = TextModel.Lines;
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        return lines.Count;
    }

    /// <summary>The rows the selection touches (a selection ending at the start of a line has not touched that line), or the cursor's row.</summary>
    private (int First, int Last) LineRangeAtCursor()
    {
        if (HasSelection)
        {
            var ((startRow, _), (endRow, endColumn)) = SelectionRange;
            var last = endColumn == 0 && endRow > startRow ? endRow - 1 : endRow;
            return ((int)startRow, (int)last);
        }

        var row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, TextModel.Lines.Count - 1);
        return (row, row);
    }

    /// <summary>
    /// Rewrites the rows <paramref name="first"/> to <paramref name="last"/> (inclusive) with <paramref name="transform"/>
    /// and records it as one undo step. The cursor moves by <paramref name="cursorDelta"/> rows; <paramref name="selectRows"/>
    /// is asked, given how many rows the transform produced, which rows to select afterwards (null for none). Returns
    /// whether the text changed.
    /// </summary>
    private bool ReplaceRows(
        int first,
        int last,
        Func<string[], string[]> transform,
        int cursorDelta,
        Func<int, (int First, int Last)?> selectRows)
    {
        var lines = TextModel.Lines;
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        var all = lines.AsSnapshot().ToArray();
        if (first < 0 || last >= all.Length || first > last)
        {
            return false;
        }

        var segment = all[first..(last + 1)];
        var replaced = transform(segment);
        if (replaced.SequenceEqual(segment, StringComparer.Ordinal))
        {
            return false;
        }

        var result = all[..first].Concat(replaced).Concat(all[(last + 1)..]).ToArray();
        if (result.Length == 0)
        {
            result = [string.Empty];
        }

        ((long Row, long Column) Start, (long Row, long Column) End)? selection = null;
        if (selectRows?.Invoke(replaced.Length) is { } rows && rows.First >= 0 && rows.First <= rows.Last && rows.Last < result.Length)
        {
            selection = ((rows.First, 0), (rows.Last, result[rows.Last].Length));
        }

        var before = Capture();
        lines.ReplaceAll(result);
        ScrollableModel.CursorPosition.Y = Math.Clamp(ScrollableModel.CursorPosition.Y + cursorDelta, 0, result.Length - 1);
        FinishLineEdit(before, selection);
        return true;
    }

    /// <summary>
    /// Rewrites the selected rows, or the whole file with nothing selected. With a selection, the rows the transform leaves
    /// stay selected.
    /// </summary>
    private bool TransformLines(Func<string[], string[]> transform)
    {
        if (IsReadOnly)
        {
            return false;
        }

        var count = EnsureLines();
        var hadSelection = HasSelection;
        var (first, last) = hadSelection ? LineRangeAtCursor() : (0, (int)count - 1);
        return ReplaceRows(
            first,
            last,
            transform,
            cursorDelta: 0,
            selectRows: produced => hadSelection && produced > 0 ? (first, first + produced - 1) : null);
    }

    /// <summary>The line with its comment marker added at <paramref name="indent"/>, or taken off when <paramref name="commented"/>; a blank line is returned as it is.</summary>
    private static string ToggledComment(string line, string marker, int indent, bool commented)
    {
        if (line.Trim().Length == 0)
        {
            return line;
        }

        if (!commented)
        {
            return line.Insert(indent, marker + " ");
        }

        var at = line.Length - line.TrimStart().Length;
        var rest = line.Remove(at, marker.Length);
        return at < rest.Length && rest[at] == ' ' ? rest.Remove(at, 1) : rest;
    }

    /// <summary>The line with each tab replaced by spaces up to the next multiple of <see cref="IndentWidth"/>.</summary>
    private static string ExpandTabs(string line)
    {
        if (!line.Contains('\t'))
        {
            return line;
        }

        var builder = new StringBuilder();
        foreach (var c in line)
        {
            if (c == '\t')
            {
                builder.Append(' ', IndentWidth - builder.Length % IndentWidth);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>The lines run together: each one's surrounding spaces trimmed and joined by a single space; a blank line adds nothing.</summary>
    private static string JoinSegment(string[] segment)
    {
        var joined = new StringBuilder(segment[0].TrimEnd(' ', '\t'));
        foreach (var line in segment.Skip(1))
        {
            var piece = line.Trim(' ', '\t');
            if (piece.Length == 0)
            {
                continue;
            }

            if (joined.Length > 0)
            {
                joined.Append(' ');
            }

            joined.Append(piece);
        }

        return joined.ToString();
    }
}
