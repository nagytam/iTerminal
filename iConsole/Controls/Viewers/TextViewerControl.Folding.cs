using iConsole.Data;

namespace iConsole.Controls.Viewers;

/// <summary>
/// Line numbers and folding for the viewer and the editor. Both live in a gutter to the left of the text and use the screen
/// rows the wrapped layout already has: a hidden (folded) line simply gets no row, and the gutter shifts the text and the
/// cursor right. A fold region is found from indentation, so it works for any language: a line folds when the next
/// non-blank line is indented deeper, and the region runs to the last line before indentation falls back.
/// </summary>
public partial class TextViewerControl
{
    private bool _showLineNumbers;
    private bool _enableFolding;
    private readonly HashSet<int> _folded = [];
    private int _foldVersion;
    private int _foldedLineCount = -1;
    private HashSet<int>? _hiddenCache;
    private int _hiddenCacheVersion = -1;

    /// <summary>Shows each line's number in a gutter on the left. Wrapped continuation rows have a blank gutter.</summary>
    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set
        {
            if (_showLineNumbers != value)
            {
                _showLineNumbers = value;
                InvalidateRows();
            }
        }
    }

    /// <summary>
    /// Shows fold markers in the gutter (<c>▾</c> open, <c>▸</c> folded) and lets Alt+Left fold and Alt+Right unfold the region
    /// at the cursor, or a click on a marker toggle it. Folds are forgotten when the number of lines changes.
    /// </summary>
    public bool EnableFolding
    {
        get => _enableFolding;
        set
        {
            if (_enableFolding != value)
            {
                _enableFolding = value;
                if (!value)
                {
                    _folded.Clear();
                }

                InvalidateRows();
            }
        }
    }

    /// <summary>True when lines are laid out as screen rows: wrapped, numbered or foldable. Otherwise a row is a line.</summary>
    public bool RowMode => _wrapLines || _showLineNumbers || _enableFolding;

    /// <summary>The line each screen row shows, top to bottom: folded lines have no row and a wrapped line has several.</summary>
    public IReadOnlyList<int> ScreenRowLines => [.. ScreenRows.Select(r => r.Line)];

    /// <summary>The width of the gutter: the fold marker and the line number with a space after it; 0 with neither.</summary>
    public int GutterWidth
    {
        get
        {
            int width = _enableFolding ? 1 : 0;
            if (_showLineNumbers)
            {
                width += Math.Max(2, TextModel.Lines.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length) + 1;
            }
            else if (_enableFolding)
            {
                width++;
            }

            return width;
        }
    }

    /// <summary>The width left for text: the view without the scroll bar column, the spare column and the gutter.</summary>
    private int TextAreaWidth => Math.Max(1, (int)ViewSize.Width - 2 - GutterWidth);

    private long _lengthSignature;

    /// <summary>
    /// Lays the rows out again when a line changed length since they were built. A typed character does not change the number of
    /// lines, which is all the row cache is keyed on, so the row of an edited line would keep its old length and cut the new text.
    /// </summary>
    private void RefreshRowsIfTextChanged()
    {
        long signature = 17;
        foreach (string line in TextModel.Lines)
        {
            signature = unchecked((signature * 31) + line.Length);
        }

        if (signature != _lengthSignature)
        {
            _lengthSignature = signature;
            _rowsKey = (-1, -1, false, 0, false);
            if (!ViewSize.IsEmpty)
            {
                _ = ScreenRows;
            }
        }
    }

    private void InvalidateRows()
    {
        _foldVersion++;
        _rowsKey = (-1, -1, false, 0, false);
        ScrollableModel.ScrollPosition.X = 0;
        _scrollableSizeStale = true;
        StateHasChanged();
    }

    /// <summary>How deeply a line is indented (a tab counts four); -1 for a blank line.</summary>
    private static int IndentOf(string line)
    {
        int indent = 0;
        foreach (char c in line)
        {
            if (c == ' ')
            {
                indent++;
            }
            else if (c == '\t')
            {
                indent += 4;
            }
            else
            {
                return indent;
            }
        }

        return -1;
    }

    /// <summary>The first and last line (inclusive) that folding <paramref name="line"/> hides, or null when it has nothing to fold.</summary>
    public (int First, int Last)? FoldRegion(int line)
    {
        IList<string> lines = TextModel.Lines;
        if (line < 0 || line >= lines.Count)
        {
            return null;
        }

        int baseIndent = IndentOf(lines[line]);
        if (baseIndent < 0)
        {
            return null;
        }

        int last = -1;
        for (int i = line + 1; i < lines.Count; i++)
        {
            int indent = IndentOf(lines[i]);
            if (indent < 0)
            {
                continue;
            }

            if (indent <= baseIndent)
            {
                break;
            }

            last = i;
        }

        return last < 0 ? null : (line + 1, last);
    }

    public bool CanFold(int line) => FoldRegion(line) is not null;

    public bool IsFolded(int line) => _folded.Contains(line);

    /// <summary>How many fold regions are closed.</summary>
    public int FoldedCount => _folded.Count;

    /// <summary>True when <paramref name="line"/> is inside a closed fold, so it has no screen row.</summary>
    public bool IsHidden(int line) => HiddenLines().Contains(line);

    private HashSet<int> HiddenLines()
    {
        if (_hiddenCache is not null && _hiddenCacheVersion == _foldVersion)
        {
            return _hiddenCache;
        }

        var hidden = new HashSet<int>();
        foreach (int start in _folded)
        {
            if (FoldRegion(start) is { } region)
            {
                for (int i = region.First; i <= region.Last; i++)
                {
                    hidden.Add(i);
                }
            }
        }

        _hiddenCache = hidden;
        _hiddenCacheVersion = _foldVersion;
        return hidden;
    }

    /// <summary>The line that owns the region <paramref name="line"/> is in: itself when it can fold, else the nearest line above that can and reaches it.</summary>
    public int? FoldOwner(int line)
    {
        if (CanFold(line))
        {
            return line;
        }

        for (int i = line - 1; i >= 0; i--)
        {
            if (FoldRegion(i) is { } region && region.Last >= line)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Closes the region of <paramref name="line"/> (or the one that holds it). The cursor, if inside, moves to the fold's first line.</summary>
    public bool Fold(int line)
    {
        if (!_enableFolding || FoldOwner(line) is not { } owner)
        {
            return false;
        }

        if (_folded.Add(owner))
        {
            ChangedFolds();
            if (ScrollableModel.CursorPosition.Y > owner && ScrollableModel.CursorPosition.Y <= FoldRegion(owner)!.Value.Last)
            {
                ScrollableModel.CursorPosition.Y = owner;
                ScrollableModel.CursorPosition.X = Math.Min(ScrollableModel.CursorPosition.X, TextModel.Lines[owner].Length);
            }
        }

        return true;
    }

    /// <summary>Opens the closed region at <paramref name="line"/>, or the outermost closed region that hides it.</summary>
    public bool Unfold(int line)
    {
        if (_folded.Remove(line))
        {
            ChangedFolds();
            return true;
        }

        foreach (int start in _folded.Order().ToList())
        {
            if (FoldRegion(start) is { } region && line >= region.First && line <= region.Last)
            {
                _folded.Remove(start);
                ChangedFolds();
                return true;
            }
        }

        return false;
    }

    public bool ToggleFold(int line)
    {
        return IsFolded(line) || IsHidden(line) ? Unfold(line) : Fold(line);
    }

    /// <summary>Closes every region that can fold (each nesting level, so the file shows only its top-level lines).</summary>
    public void FoldAll()
    {
        if (!_enableFolding)
        {
            return;
        }

        for (int i = 0; i < TextModel.Lines.Count; i++)
        {
            if (CanFold(i))
            {
                _folded.Add(i);
            }
        }

        ChangedFolds();
        MoveCursorOutOfFolds();
    }

    public void UnfoldAll()
    {
        if (_folded.Count > 0)
        {
            _folded.Clear();
            ChangedFolds();
        }
    }

    /// <summary>Makes <paramref name="line"/> visible by opening every fold that hides it.</summary>
    internal void RevealLine(int line)
    {
        while (IsHidden(line) && Unfold(line))
        {
        }
    }

    private void ChangedFolds()
    {
        _foldVersion++;
        _foldedLineCount = TextModel.Lines.Count;
        _rowsKey = (-1, -1, false, 0, false);
        StateHasChanged();
    }

    /// <summary>Forgets the folds when lines were added or removed, since they are kept by line number.</summary>
    private void DropStaleFolds()
    {
        if (_folded.Count > 0 && TextModel.Lines.Count != _foldedLineCount)
        {
            _folded.Clear();
            ChangedFolds();
        }
    }

    /// <summary>Puts the cursor on the first line of a fold when it is on a line the fold hides.</summary>
    internal void MoveCursorOutOfFolds()
    {
        if (_folded.Count == 0 || TextModel.Lines.Count == 0)
        {
            return;
        }

        int row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, TextModel.Lines.Count - 1);
        if (IsHidden(row) && FoldOwner(row) is { } owner)
        {
            // The owner of an inner line may itself be hidden by an outer fold; climb until a visible line is found.
            while (IsHidden(owner) && FoldOwner(owner - 1) is { } outer && outer < owner)
            {
                owner = outer;
            }

            ScrollableModel.CursorPosition.Y = owner;
            ScrollableModel.CursorPosition.X = Math.Min(ScrollableModel.CursorPosition.X, TextModel.Lines[owner].Length);
        }
    }

    /// <summary>The gutter text of a screen row: fold marker, right-aligned number and a space; blank for a wrapped continuation row.</summary>
    internal string GutterText(TextRow row)
    {
        int numberWidth = _showLineNumbers ? GutterWidth - (_enableFolding ? 1 : 0) - 1 : 0;
        string marker = string.Empty;
        string number = string.Empty;
        if (row.Start == 0)
        {
            if (_enableFolding)
            {
                marker = IsFolded(row.Line) ? "▸" : CanFold(row.Line) ? "▾" : " ";
            }

            if (_showLineNumbers)
            {
                number = (row.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(numberWidth) + " ";
            }
            else if (_enableFolding)
            {
                number = " ";
            }
        }
        else
        {
            marker = _enableFolding ? " " : string.Empty;
            number = new string(' ', Math.Max(0, GutterWidth - marker.Length));
        }

        return marker + number;
    }

    /// <summary>Alt+Left folds the region at the cursor line and Alt+Right opens it.</summary>
    private bool HandleFoldKey(KeyEvent keyEvent)
    {
        if (!_enableFolding || !IsEnabled || !keyEvent.Alt || keyEvent.Control || keyEvent.Shift
            || keyEvent.ConsoleKey is not (ConsoleKey.LeftArrow or ConsoleKey.RightArrow) || TextModel.Lines.Count == 0)
        {
            return false;
        }

        int line = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, TextModel.Lines.Count - 1);
        bool changed = keyEvent.ConsoleKey == ConsoleKey.LeftArrow ? Fold(line) : Unfold(line);
        if (changed)
        {
            KeepCursorInText();
        }

        return changed;
    }
}
