using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Helper;
using iConsole.Data;
using iConsole.Display;
using iConsole.Controls.Viewers;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls.Editors;

public class TextEditorControl 
    : TextViewerControl, IDirtyModel, IDirtyReadOnlyModel, IReadOnly
{
    private bool _isDirty = false;
    public bool IsDirty
    {
        get
        {
            return _isDirty;
        }
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    private bool _isReadOnly = false;
    public bool IsReadOnly
    {
        get
        {
            return _isReadOnly;
        }
        set
        {
            if (value != _isReadOnly)
            {
                _isReadOnly = value;
                OnPropertyChanged(nameof(IsReadOnly));
            }
        }
    }

    public TextEditorControl(
        IThemeService themeService)
        : base(true, themeService)
    {
        IsCursorVisible = true;
        CursorShape = CursorShape.BlinkingBar;
        PointerShape = PointerShape.Text;

        AddCommand(new TypeCommand());
        AddCommand(new BackspaceCommand(TextModel, ScrollableModel));
        AddCommand(new DeleteCharacterCommand(TextModel, ScrollableModel));
        AddCommand(new DeleteLineCommand(TextModel, ScrollableModel));
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    private ITextClipboardService? _clipboard;

    /// <summary>The clipboard Copy, Cut and Paste use: the application's (<see cref="TextClipboard.Current"/>) unless one is given.</summary>
    public ITextClipboardService Clipboard
    {
        get => _clipboard ?? TextClipboard.Current;
        set => _clipboard = value;
    }

    /// <summary>
    /// Puts <paramref name="text"/> in at the cursor, in place of a selection: its lines become lines of the text, the
    /// first joined to what is before the cursor and the last to what is after it. Line breaks are normalised and
    /// control characters other than a tab are dropped.
    /// </summary>
    public void InsertText(string? text)
    {
        if (IsReadOnly || string.IsNullOrEmpty(text))
        {
            return;
        }

        var cleaned = new string([.. ClipboardKeys.NormalizeLineBreaks(text).Where(c => c is '\n' or '\t' || !char.IsControl(c))]);
        if (cleaned.Length == 0)
        {
            return;
        }

        if (HasSelection)
        {
            RemoveSelectedText();
        }

        var lines = TextModel.Lines;
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        var row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, lines.Count - 1);
        var line = lines[row];
        var column = (int)Math.Min(ScrollableModel.CursorPosition.X, line.Length);
        var before = line[..column];
        var after = line[column..];
        var parts = cleaned.Split('\n');
        if (parts.Length == 1)
        {
            lines[row] = before + parts[0] + after;
            ScrollableModel.CursorPosition.X = column + parts[0].Length;
        }
        else
        {
            lines[row] = before + parts[0];
            for (var i = 1; i < parts.Length - 1; i++)
            {
                lines.Insert(row + i, parts[i]);
            }

            lines.Insert(row + parts.Length - 1, parts[^1] + after);
            ScrollableModel.CursorPosition.Y = row + parts.Length - 1;
            ScrollableModel.CursorPosition.X = parts[^1].Length;
        }

        IsDirty = true;
        TextModel.TriggerPropertyChanged();
        Refresh();
        KeepCursorInText();
    }

    /// <summary>Copy, Cut, Paste and Select all, on the keys <see cref="ClipboardKeys"/> names. A key the editor cannot act on is left alone.</summary>
    private async Task<bool> HandleClipboardKeyAsync(KeyEvent keyEvent)
    {
        switch (ClipboardKeys.ActionFor(keyEvent))
        {
            case ClipboardKeyAction.SelectAll:
                if (TextModel.Lines.All(l => l.Length == 0))
                {
                    return false;
                }

                SelectAll();
                return true;
            case ClipboardKeyAction.Copy when HasSelection:
                await ClipboardKeys.TrySetAsync(Clipboard, SelectedText);
                return true;
            case ClipboardKeyAction.Cut when HasSelection:
                await ClipboardKeys.TrySetAsync(Clipboard, SelectedText);
                if (!IsReadOnly)
                {
                    RemoveSelectedText();
                    IsDirty = true;
                }

                return true;
            case ClipboardKeyAction.Paste when !IsReadOnly:
                var text = await ClipboardKeys.TryGetAsync(Clipboard);
                if (string.IsNullOrEmpty(text))
                {
                    return false;
                }

                InsertText(text);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// With text selected, Backspace and Delete remove it, and typing or Enter replaces it (the key then goes on to
    /// type or split the line at the cursor the removal left). A read-only editor can select but not change.
    /// </summary>
    private async Task<bool> HandleEditKeyAsync(KeyEvent keyEvent)
    {
        if (IsEnabled && await HandleClipboardKeyAsync(keyEvent))
        {
            return true;
        }

        if (HasSelection && !IsReadOnly && IsEnabled && !keyEvent.Control && !keyEvent.Alt)
        {
            if (!keyEvent.Shift && keyEvent.ConsoleKey is ConsoleKey.Backspace or ConsoleKey.Delete)
            {
                RemoveSelectedText();
                IsDirty = true;
                return true;
            }

            if (ConsoleKeyHelper.IsTypingKey(keyEvent) || keyEvent.ConsoleKey == ConsoleKey.Enter)
            {
                RemoveSelectedText();
                IsDirty = true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    // ---- Indenting ----

    /// <summary>How far Tab moves, in spaces: the width the project's own files are indented with.</summary>
    public const int IndentWidth = 4;

    /// <summary>
    /// Tab puts spaces up to the next multiple of <see cref="IndentWidth"/> (a tab character would be drawn one cell
    /// wide); with a selection it indents every line the selection touches. Shift+Tab takes one step of indent off the
    /// line, or each selected line — a tab character or up to <see cref="IndentWidth"/> spaces. Returns whether it acted.
    /// </summary>
    public bool Indent(bool outdent)
    {
        if (IsReadOnly)
        {
            return false;
        }

        var lines = TextModel.Lines;
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        ((long Row, long Column) Anchor, (long Row, long Column) Cursor)? selectAfter = null;
        if (HasSelection)
        {
            var ((startRow, _), (endRow, endColumn)) = SelectionRange;
            // A selection that ends at the very start of a line has not touched that line.
            var lastRow = endColumn == 0 && endRow > startRow ? endRow - 1 : endRow;
            for (var row = startRow; row <= lastRow; row++)
            {
                lines[(int)row] = outdent ? Outdented(lines[(int)row], out _) : IndentWidthSpaces + lines[(int)row];
            }

            selectAfter = ((startRow, 0), (lastRow, lines[(int)lastRow].Length));
        }
        else
        {
            var row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, lines.Count - 1);
            var column = (int)Math.Min(ScrollableModel.CursorPosition.X, lines[row].Length);
            if (outdent)
            {
                lines[row] = Outdented(lines[row], out var removed);
                ScrollableModel.CursorPosition.X = Math.Max(0, column - removed);
            }
            else
            {
                var spaces = IndentWidth - (column % IndentWidth);
                lines[row] = lines[row].Insert(column, new string(' ', spaces));
                ScrollableModel.CursorPosition.X = column + spaces;
            }

            ScrollableModel.CursorPosition.Y = row;
        }

        IsDirty = true;
        TextModel.TriggerPropertyChanged();
        Refresh();
        if (selectAfter is { } selection)
        {
            // After the refresh, which would otherwise end the selection: the lines stay selected, so Tab can go on.
            SelectRange(selection.Anchor, selection.Cursor);
        }

        return true;
    }

    private static readonly string IndentWidthSpaces = new(' ', IndentWidth);

    /// <summary>The line with one step of indent off its start; <paramref name="removed"/> is how many characters that was.</summary>
    private static string Outdented(string line, out int removed)
    {
        if (line.StartsWith('\t'))
        {
            removed = 1;
            return line[1..];
        }

        var spaces = 0;
        while (spaces < IndentWidth && spaces < line.Length && line[spaces] == ' ')
        {
            spaces++;
        }

        removed = spaces;
        return line[spaces..];
    }

    // ---- Undo and redo ----

    /// <summary>The text, and where the cursor was, at one point of the editing.</summary>
    private sealed record EditState(string[] Lines, long Row, long Column);

    /// <summary>The most edits that can be taken back.</summary>
    private const int HistoryLimit = 200;

    private readonly List<EditState> _undo = [];
    private readonly List<EditState> _redo = [];

    /// <summary>Where the last typed character left the cursor: typing on from there joins the same undo step.</summary>
    private (long Row, long Column)? _typingEnd;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    private EditState Capture() => new(
        TextModel.Lines.AsSnapshot().ToArray(),
        ScrollableModel.CursorPosition.Y,
        ScrollableModel.CursorPosition.X);

    private bool IsChangedFrom(EditState before)
    {
        var now = TextModel.Lines.AsSnapshot();
        if (now.Count != before.Lines.Length)
        {
            return true;
        }

        for (var i = 0; i < before.Lines.Length; i++)
        {
            if (!string.Equals(before.Lines[i], now[i], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void PushUndo(EditState state)
    {
        _undo.Add(state);
        if (_undo.Count > HistoryLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    /// <summary>Takes the last edit back. Typing that ran on without a break is one edit.</summary>
    public bool Undo()
    {
        if (_undo.Count == 0 || IsReadOnly)
        {
            return false;
        }

        var current = Capture();
        var target = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        Restore(target, current);
        return true;
    }

    /// <summary>Does again what <see cref="Undo"/> took back.</summary>
    public bool Redo()
    {
        if (_redo.Count == 0 || IsReadOnly)
        {
            return false;
        }

        var current = Capture();
        var target = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        Restore(target, current);
        return true;
    }

    private void Restore(EditState target, EditState from)
    {
        TextModel.Lines.ReplaceAll(target.Lines);
        ClearSelection();
        ShiftBookmarks(from.Lines, target.Lines);
        _typingEnd = null;
        IsDirty = true;
        TextModel.TriggerPropertyChanged();
        Refresh();
        ScrollableModel.CursorPosition.Y = target.Row;
        ScrollableModel.CursorPosition.X = target.Column;
        KeepCursorInText();
        StateHasChanged();
    }

    /// <summary>
    /// Makes what changed in the text since <paramref name="before"/> one undo step, and moves the bookmarks below it
    /// with their lines. Nothing changed, nothing recorded.
    /// </summary>
    private void RecordEdit(EditState before, bool isTyping)
    {
        if (!IsChangedFrom(before))
        {
            if (!isTyping)
            {
                _typingEnd = null;
            }

            return;
        }

        var joinsTyping = isTyping && _typingEnd is { } end && end == (before.Row, before.Column);
        if (!joinsTyping)
        {
            PushUndo(before);
        }

        ShiftBookmarks(before.Lines, TextModel.Lines.AsSnapshot().ToArray());
        _typingEnd = isTyping
            ? (ScrollableModel.CursorPosition.Y, ScrollableModel.CursorPosition.X)
            : null;
    }

    // ---- Replace ----

    /// <summary>What the replace dialog was last asked to find, and to put in its place; it opens with these.</summary>
    public string LastFind { get; set; } = string.Empty;

    public string LastReplacement { get; set; } = string.Empty;

    public bool LastMatchCase { get; set; }

    private static StringComparison ComparisonFor(bool matchCase) =>
        matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Replaces the first occurrence of <paramref name="find"/> at or after the cursor — wrapping round to the start of
    /// the text — with <paramref name="replacement"/>, and leaves the cursor just after it, so asking again goes on to
    /// the next one. Returns whether there was one. A match is within one line.
    /// </summary>
    public bool ReplaceNext(string find, string replacement, bool matchCase = true)
    {
        if (IsReadOnly || string.IsNullOrEmpty(find) || find.Contains('\n') || TextModel.Lines.Count == 0)
        {
            return false;
        }

        var lines = TextModel.Lines;
        var comparison = ComparisonFor(matchCase);
        var startRow = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, lines.Count - 1);
        var startColumn = (int)Math.Min(ScrollableModel.CursorPosition.X, lines[startRow].Length);
        for (var step = 0; step <= lines.Count; step++)
        {
            var row = (startRow + step) % lines.Count;
            var line = lines[row];
            var from = step == 0 ? startColumn : 0;
            var at = from <= line.Length ? line.IndexOf(find, from, comparison) : -1;
            // Coming back round to the starting line, only what is before the cursor is left to look at.
            if (step == lines.Count && (at < 0 || at >= startColumn))
            {
                return false;
            }

            if (at < 0)
            {
                continue;
            }

            var before = Capture();
            lines[row] = string.Concat(line.AsSpan(0, at), replacement, line.AsSpan(at + find.Length));
            ScrollableModel.CursorPosition.Y = row;
            ScrollableModel.CursorPosition.X = at + replacement.Length;
            AfterReplace(before);
            ShowLine(row, at + replacement.Length);
            return true;
        }

        return false;
    }

    /// <summary>Replaces every occurrence of <paramref name="find"/> in the text as one undo step; returns how many.</summary>
    public int ReplaceAll(string find, string replacement, bool matchCase = true)
    {
        if (IsReadOnly || string.IsNullOrEmpty(find) || find.Contains('\n') || TextModel.Lines.Count == 0)
        {
            return 0;
        }

        var comparison = ComparisonFor(matchCase);
        var before = Capture();
        var count = 0;
        var lines = TextModel.Lines;
        for (var row = 0; row < lines.Count; row++)
        {
            var line = lines[row];
            var at = 0;
            var inLine = 0;
            while ((at = line.IndexOf(find, at, comparison)) >= 0)
            {
                inLine++;
                at += find.Length;
            }

            if (inLine > 0)
            {
                lines[row] = line.Replace(find, replacement, comparison);
                count += inLine;
            }
        }

        if (count > 0)
        {
            AfterReplace(before);
        }

        return count;
    }

    private void AfterReplace(EditState before)
    {
        ClearSelection();
        SearchModel.Hits.Clear();
        RecordEdit(before, isTyping: false);
        IsDirty = true;
        TextModel.TriggerPropertyChanged();
        Refresh();
        KeepCursorInText();
        StateHasChanged();
    }

    // ---- Bookmarks ----

    private readonly SortedSet<int> _bookmarks = [];

    /// <summary>The lines that carry a bookmark, counting from 0, in order.</summary>
    public IReadOnlyCollection<int> Bookmarks => _bookmarks;

    /// <summary>Puts a bookmark on the cursor's line, or takes the one there off. Returns whether the line is marked now.</summary>
    public bool ToggleBookmark()
    {
        if (TextModel.Lines.Count == 0)
        {
            return false;
        }

        var row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, TextModel.Lines.Count - 1);
        var isMarked = !_bookmarks.Remove(row);
        if (isMarked)
        {
            _bookmarks.Add(row);
        }

        StateHasChanged();
        return isMarked;
    }

    /// <summary>Goes to the next bookmark below the cursor's line, or the previous one above it, wrapping round. Returns false when there is none.</summary>
    public bool GoToBookmark(bool forward = true)
    {
        if (_bookmarks.Count == 0 || TextModel.Lines.Count == 0)
        {
            return false;
        }

        var row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, TextModel.Lines.Count - 1);
        int target = forward
            ? _bookmarks.FirstOrDefault(b => b > row, _bookmarks.Min)
            : _bookmarks.LastOrDefault(b => b < row, _bookmarks.Max);
        ClearSelection();
        ShowLine(target, ScrollableModel.CursorPosition.X);
        return true;
    }

    /// <summary>Moves the bookmarks with their lines after the text changed from <paramref name="oldLines"/> to <paramref name="newLines"/>: the lines the edit left alone keep theirs.</summary>
    private void ShiftBookmarks(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines)
    {
        if (_bookmarks.Count == 0)
        {
            return;
        }

        var shortest = Math.Min(oldLines.Count, newLines.Count);
        var prefix = 0;
        while (prefix < shortest && string.Equals(oldLines[prefix], newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < shortest - prefix
            && string.Equals(oldLines[oldLines.Count - 1 - suffix], newLines[newLines.Count - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
        }

        var oldEnd = oldLines.Count - suffix;
        var newEnd = newLines.Count - suffix;
        var delta = newLines.Count - oldLines.Count;
        var moved = new List<int>(_bookmarks.Count);
        foreach (var bookmark in _bookmarks)
        {
            if (bookmark < prefix)
            {
                moved.Add(bookmark);
            }
            else if (bookmark >= oldEnd)
            {
                moved.Add(bookmark + delta);
            }
            else if (bookmark < newEnd)
            {
                // Inside the lines the edit rewrote, and still there.
                moved.Add(bookmark);
            }
        }

        _bookmarks.Clear();
        foreach (var bookmark in moved)
        {
            _bookmarks.Add(bookmark);
        }
    }

    /// <summary>
    /// Whether the bracket at the cursor and its partner are marked, and Ctrl+F9 goes to the partner. On by default.
    /// </summary>
    public bool BracketMatchingEnabled { get; set; } = true;

    private BracketMatch? _bracketMatch;

    /// <summary>The bracket at (or just before) the cursor and the one it pairs with, or null when the cursor is not by a bracket.</summary>
    public BracketMatch? FindMatchingBracket()
    {
        var row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, Math.Max(0, TextModel.Lines.Count - 1));
        return BracketMatcher.Find(TextModel.Lines, row, (int)ScrollableModel.CursorPosition.X);
    }

    /// <summary>Moves the cursor onto the bracket that pairs with the one at the cursor. False when there is none to go to.</summary>
    public bool GoToMatchingBracket()
    {
        if (FindMatchingBracket() is not { Match: { } match })
        {
            return false;
        }

        ScrollableModel.CursorPosition.Y = match.Line;
        ScrollableModel.CursorPosition.X = match.Column;
        ClearSelection();
        KeepCursorInText();
        StateHasChanged();
        return true;
    }

    public override void Draw(IBuffer buffer)
    {
        // Looked for once a frame, not once a row: the search can cross many lines.
        _bracketMatch = BracketMatchingEnabled ? FindMatchingBracket() : null;
        base.Draw(buffer);
    }

    protected override void DecorateText(ConxelString drawn, int line, int firstColumn, bool isCursorLine, ColorScheme colorScheme)
    {
        if (_bracketMatch is null)
        {
            return;
        }

        // A bracket with a partner is marked together with it; one without is marked alone, in red.
        bool paired = _bracketMatch.Match is not null;
        MarkBracket(drawn, _bracketMatch.Bracket, line, firstColumn, paired);
        if (_bracketMatch.Match is { } match)
        {
            MarkBracket(drawn, match, line, firstColumn, paired);
        }
    }

    private static void MarkBracket(ConxelString drawn, BracketLocation location, int line, int firstColumn, bool paired)
    {
        int cell = location.Column - firstColumn;
        if (location.Line != line || cell < 0 || cell >= drawn.Count)
        {
            return;
        }

        Conxel conxel = drawn.String[cell];
        conxel.ForegroundColor = paired ? Color.Black : Color.White;
        conxel.BackgroundColor = paired ? Color.Cyan : Color.Red;
        conxel.IsBold = true;
        drawn.String[cell] = conxel;
    }

    protected override void DecorateLine(ConxelString drawn, int line, bool isCursorLine, ColorScheme colorScheme)
    {
        if (!_bookmarks.Contains(line) || drawn.Count == 0)
        {
            return;
        }

        if (isCursorLine)
        {
            // The cursor's row is yellow throughout, so the mark is its first cell.
            var cell = drawn.String[0];
            cell.BackgroundColor = Color.Red;
            cell.ForegroundColor = Color.White;
            drawn.String[0] = cell;
        }
        else
        {
            drawn.HighlightRange(0, drawn.Count, Color.White, Color.DarkCyan);
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Ctrl+A", "Select all");
        yield return ("Ctrl+C", "Copy the selection");

        // A read-only editor refuses the changes to the text, so only the keys that move about, copy or save are offered.
        if (!IsReadOnly)
        {
            yield return ("Ctrl+X", "Cut the selection");
            yield return ("Ctrl+V", "Paste");
            yield return ("Ctrl+Z", "Undo");
            yield return ("Ctrl+Y", "Redo");
            yield return ("Ctrl+R", "Replace");
            yield return ("Tab / Shift+Tab", "Indent / outdent (the selected lines too)");
            yield return ("Ctrl+K", "Delete the line");
        }

        yield return ("Ctrl+S / F2", "Save");
        yield return ("F5", "Go to line");
        yield return ("Ctrl+F2", "Toggle bookmark");
        yield return ("F9", "Next bookmark");
        yield return ("Shift+F9", "Previous bookmark");
        if (EnableFolding)
        {
            yield return ("Alt+Left/Alt+Right", "Fold or unfold the region at the cursor");
        }

        if (BracketMatchingEnabled)
        {
            yield return ("Ctrl+F9", "Go to the matching bracket");
        }
    }

    /// <summary>
    /// Undo and redo (Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z), the bookmark keys (Ctrl+F2 sets or clears one, F9 and
    /// Shift+F9 go to the next and the previous), and every other key as an edit that can be taken back.
    /// </summary>
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // Wrapped, Up and Down move by screen row, so a long line is walked row by row; by line they would skip its rows.
        if (IsEnabled && RowMode && !HasSelection && !keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt
            && keyEvent.ConsoleKey is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
        {
            MoveCursorByScreenRow(keyEvent.ConsoleKey == ConsoleKey.DownArrow ? 1 : -1);
            return true;
        }

        if (IsEnabled && !keyEvent.Alt)
        {
            if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.Z)
            {
                if (keyEvent.Shift)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }

                return true;
            }

            if (keyEvent.Control && !keyEvent.Shift && keyEvent.ConsoleKey == ConsoleKey.Y)
            {
                Redo();
                return true;
            }

            if (keyEvent.Control && !keyEvent.Shift && keyEvent.ConsoleKey == ConsoleKey.F2)
            {
                ToggleBookmark();
                return true;
            }

            if (!keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.F9)
            {
                GoToBookmark(forward: !keyEvent.Shift);
                return true;
            }

            if (keyEvent.Control && !keyEvent.Shift && keyEvent.ConsoleKey == ConsoleKey.F9 && BracketMatchingEnabled)
            {
                GoToMatchingBracket();
                return true;
            }

            // Tab is the editor's: it indents. The dialog's Tab (to the next control) has nothing else to reach.
            if (!keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.Tab)
            {
                var beforeIndent = Capture();
                Indent(outdent: keyEvent.Shift);
                RecordEdit(beforeIndent, isTyping: false);
                return true;
            }
        }

        var before = Capture();
        var isTyping = ConsoleKeyHelper.IsTypingKey(keyEvent) && !HasSelection;
        var handled = await HandleEditKeyAsync(keyEvent);
        RecordEdit(before, isTyping);
        return handled;
    }

    public override void Refresh()
    {
        if (TextModel.Lines.Count > 0)
        {
            ScrollableModel.ScrollableSize = new Data.Size(
                TextModel.Lines.Max(l => l.Length),
                TextModel.Lines.Count);
        }
        else
        {
            ScrollableModel.ScrollableSize = new Data.Size(0, 0);
        }
        KeepCursorInText();
        base.Refresh();
    }
}
