using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Layouts;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls.Viewers;

public partial class TextViewerControl : Control
{
    public IScrollableModel ScrollableModel { get; private set; } = new ScrollableModel();
    public ISearchModel SearchModel { get; private set; } = new SearchModel();
    public ITextModel TextModel { get; private set; } = new TextModel();
    public ISelectionTextModel SelectionTextModel { get; private set; } = new SelectionTextModel();
    private readonly bool _isCursorEnabled;
    private ScrollBar.ScrollBar _verticalScrollBar { get; set; }
    private ScrollBar.ScrollBar _horizontalScrollBar { get; set; }
    public override Position CursorPosition
    {
        get
        {
            Position cursorPosition;
            if (IsEnabled && IsVisible)
            {
                if (RowMode)
                {
                    // Wrapped, the scroll counts screen rows and the caret sits at its column within the row it is on.
                    int row = CursorScreenRow();
                    cursorPosition = new Position(
                        ScrollableModel.CursorPosition.X - ScreenRows[row].Start + AbsolutePosition.X + GutterWidth,
                        row - ScrollableModel.ScrollPosition.Y + AbsolutePosition.Y);
                }
                else
                {
                    cursorPosition = new Position(
                        ScrollableModel.CursorPosition.X - ScrollableModel.ScrollPosition.X + AbsolutePosition.X,
                        ScrollableModel.CursorPosition.Y - ScrollableModel.ScrollPosition.Y + AbsolutePosition.Y
                        );
                }
            }
            else
            {
                cursorPosition = new Position(0, 0);
            }
            return cursorPosition;
        }
        set
        {
            if (ScrollableModel != null && value is not null)
            {
                ScrollableModel.CursorPosition.X = value.X + AbsolutePosition.X + ScrollableModel.ScrollPosition.X;
                ScrollableModel.CursorPosition.Y = value.Y + AbsolutePosition.Y + ScrollableModel.ScrollPosition.Y;
            }
            else if (ScrollableModel != null && value is null)
            {
                ScrollableModel.CursorPosition = new(0,0);
            }
        }
    }

    public TextViewerControl(bool isCursorEnabled, IThemeService themeService) 
        : base(themeService)
    {
        _isCursorEnabled = isCursorEnabled;
        ScrollableModel.PropertyChanged += OnPropertyChanged;
        _verticalScrollBar = new ScrollBar.ScrollBar(themeService) { ScrollBarOrientation = Orientation.Vertical };
        _horizontalScrollBar = new ScrollBar.ScrollBar(themeService) { ScrollBarOrientation = Orientation.Horizontal };
        IsCursorVisible = isCursorEnabled;

        AddControl(_verticalScrollBar);
        AddControl(_horizontalScrollBar);
        // A viewer has no caret, so Home and End take it to the start and the end of the file; an editor keeps them for
        // the line, and takes Ctrl+Home / Ctrl+End for the file.
        CommandHelper.AddVerticalScrollingCommands(this, ScrollableModel, !isCursorEnabled);
        AddCommand(new ScrollToHomeVerticalCommand(ScrollableModel) { Key = new Key(ConsoleKey.Home, false, false, true) });
        AddCommand(new ScrollToEndVerticalCommand(ScrollableModel) { Key = new Key(ConsoleKey.End, false, false, true) });
        CommandHelper.AddHorizontalScrollingCommands(this, ScrollableModel, TextModel, SelectionTextModel, isCursorEnabled);
        TextModel.PropertyChanged += (_, _) =>
        {
            _scrollableSizeStale = true;
            DropStaleFolds();
        };
    }

    // ---- Scrollable size (the text can change after the layout) ----

    private bool _scrollableSizeStale = true;
    private ConcurrentList<string>? _sizedLines;
    private int _sizedCount = -1;

    /// <summary>
    /// Brings <see cref="IScrollableModel.ScrollableSize"/> up to the text. The layout sets it once, but a file can be
    /// read, or re-read under another code page, after that, and a size left at zero lets nothing scroll. The longest
    /// line is only searched for again when the text was announced as changed or its line count differs.
    /// </summary>
    protected void SyncScrollableSize()
    {
        var lines = TextModel.Lines;
        if (!_scrollableSizeStale && ReferenceEquals(lines, _sizedLines) && lines.Count == _sizedCount)
        {
            return;
        }

        _scrollableSizeStale = false;
        _sizedLines = lines;
        _sizedCount = lines.Count;
        if (RowMode && !ViewSize.IsEmpty)
        {
            _rowsKey = (-1, -1, false, 0, false);
            _ = ScreenRows;
            return;
        }

        var size = lines.Count == 0
            ? new Size(0, 0)
            : new Size(lines.Max(l => l.Length), lines.Count);
        ScrollableModel.ScrollableSize = size;
        ClampScrollPosition();
    }

    /// <summary>Keeps the view inside the text: no scrolling past the last line, or past the end of the longest one.</summary>
    private void ClampScrollPosition()
    {
        if (ScrollableModel.ViewSize.IsEmpty)
        {
            return;
        }

        long maxY = Math.Max(0, ScrollableModel.ScrollableSize.Height - ScrollableModel.ViewSize.Height);
        long maxX = Math.Max(0, ScrollableModel.ScrollableSize.Width - ScrollableModel.ViewSize.Width);
        if (ScrollableModel.ScrollPosition.Y > maxY)
        {
            ScrollableModel.ScrollPosition.Y = maxY;
        }

        if (ScrollableModel.ScrollPosition.X > maxX)
        {
            ScrollableModel.ScrollPosition.X = maxX;
        }
    }

    // ---- Syntax colouring ----

    private SyntaxLanguage? _syntax;

    /// <summary>The language the text is coloured as; null for none. Set by the dialog from the file's extension.</summary>
    public SyntaxLanguage? Syntax
    {
        get => _syntax;
        set
        {
            if (!ReferenceEquals(_syntax, value))
            {
                _syntax = value;
                StateHasChanged();
            }
        }
    }

    /// <summary>How far back a block comment is looked for when the first row on screen is drawn.</summary>
    private const int BlockCommentLookBack = 400;

    private readonly List<SyntaxSpan> _spans = [];

    /// <summary>Whether line <paramref name="line"/> starts inside a block comment, found by reading a bounded stretch of lines before it.</summary>
    private bool StartsInBlockComment(int line)
    {
        if (_syntax?.BlockComment is null)
        {
            return false;
        }

        bool inComment = false;
        for (int i = Math.Max(0, line - BlockCommentLookBack); i < line && i < TextModel.Lines.Count; i++)
        {
            _spans.Clear();
            inComment = Highlighting.Tokenize(_syntax, TextModel.Lines[i], inComment, _spans);
        }

        return inComment;
    }

    /// <summary>Where languages are looked up and which colours they get; the shared service unless one is given.</summary>
    public ISyntaxHighlightingService Highlighting { get; set; } = SyntaxHighlightingService.Default;

    private Color ColorOf(SyntaxKind kind) => Highlighting.Palette.ColorOf(kind, Color.White);

    /// <summary>Colours the keywords, strings, comments and numbers of line <paramref name="line"/> in a row drawn from the scroll position on.</summary>
    private void ColorizeSyntax(ConxelString drawn, string text, bool startsInComment, int scrollX, out bool endsInComment, bool skipColours)
    {
        endsInComment = false;
        if (_syntax is null)
        {
            return;
        }

        _spans.Clear();
        endsInComment = Highlighting.Tokenize(_syntax, text, startsInComment, _spans);
        if (skipColours)
        {
            return;
        }

        ApplySpans(drawn, _spans, scrollX);
    }

    /// <summary>Colours the cells of <paramref name="drawn"/>, which starts at character <paramref name="offset"/> of its line, by what each span is.</summary>
    private void ApplySpans(ConxelString drawn, List<SyntaxSpan> spans, int offset)
    {
        foreach (SyntaxSpan span in spans)
        {
            int start = Math.Max(0, span.Start - offset);
            int end = Math.Min(drawn.Count, span.Start + span.Length - offset);
            Color color = ColorOf(span.Kind);
            for (int x = start; x < end; x++)
            {
                Conxel cell = drawn.String[x];
                cell.ForegroundColor = color;
                drawn.String[x] = cell;
            }
        }
    }

    /// <summary>
    /// A hook for what an editor adds to a drawn row — the bookmark mark, say. <paramref name="line"/> is the text line;
    /// <paramref name="drawn"/> starts at the scroll position.
    /// </summary>
    protected virtual void DecorateLine(ConxelString drawn, int line, bool isCursorLine, ColorScheme colorScheme)
    {
    }

    /// <summary>
    /// A hook for what an editor marks inside a row's text — a bracket and its partner, say. Called for wrapped and unwrapped
    /// rows alike. <paramref name="firstColumn"/> is the text column the first cell of <paramref name="drawn"/> shows, so a
    /// mark at column <c>c</c> is the cell <c>c - firstColumn</c>.
    /// </summary>
    protected virtual void DecorateText(ConxelString drawn, int line, int firstColumn, bool isCursorLine, ColorScheme colorScheme)
    {
    }

    /// <summary>
    /// With a caret (an editor), brings the cursor back onto the text it is editing: a row that exists, a column no
    /// further than the end of that line — Up/Down onto a shorter line, or Right across a short one, used to leave it
    /// floating in the empty space — and the view scrolled so it is on screen.
    /// </summary>
    protected void KeepCursorInText()
    {
        if (!_isCursorEnabled || TextModel.Lines.Count == 0 || ScrollableModel.ViewSize.IsEmpty)
        {
            return;
        }

        MoveCursorOutOfFolds();
        var row = Math.Clamp(ScrollableModel.CursorPosition.Y, 0, TextModel.Lines.Count - 1);
        ScrollableModel.CursorPosition.Y = row;
        ScrollableModel.CursorPosition.X = Math.Clamp(ScrollableModel.CursorPosition.X, 0, TextModel.Lines[(int)row].Length);
        if (RowMode)
        {
            KeepRowVisible(CursorScreenRow());
            return;
        }

        ScrollableModel.ScrollToVisible();
    }

    // ---- Wrapping (the viewer; an editor keeps each line whole) ----

    private bool _wrapLines;

    /// <summary>
    /// Wraps long lines to the width of the view, so every part of a line can be read without scrolling sideways. The
    /// vertical scroll then counts screen rows; the cursor stays on its line, and the line's rows are highlighted together.
    /// </summary>
    public bool WrapLines
    {
        get => _wrapLines;
        set
        {
            if (_wrapLines == value)
            {
                return;
            }

            _wrapLines = value;
            ScrollableModel.ScrollPosition.X = 0;
            _rowsKey = (-1, -1, false, 0, false);
            StateHasChanged();
        }
    }

    /// <summary>One screen row: the line it shows, and the part of that line it shows.</summary>
    internal readonly record struct TextRow(int Line, int Start, int Length);

    private List<TextRow> _rows = [];
    private (int Lines, int Width, bool Wrap, int Fold, bool Numbers) _rowsKey = (-1, -1, false, 0, false);

    /// <summary>The screen rows for the current text and width. Rebuilt when either changes.</summary>
    internal IReadOnlyList<TextRow> ScreenRows
    {
        get
        {
            int width = TextAreaWidth;
            if (_rowsKey != (TextModel.Lines.Count, width, _wrapLines, _foldVersion, _showLineNumbers))
            {
                RebuildRows(width);
            }

            return _rows;
        }
    }

    private void RebuildRows(int width)
    {
        // The row at the top of the screen is kept as text, so a resize (or a change of wrapping) re-flows the lines around
        // the same reading position instead of leaving the scroll on whatever text now has the old row number.
        TextRow? top = _rows.Count > 0 && ScrollableModel.ScrollPosition.Y >= 0 && ScrollableModel.ScrollPosition.Y < _rows.Count
            ? _rows[(int)ScrollableModel.ScrollPosition.Y]
            : null;

        _rows = new List<TextRow>(TextModel.Lines.Count);
        HashSet<int> hidden = HiddenLines();
        for (int line = 0; line < TextModel.Lines.Count; line++)
        {
            if (hidden.Contains(line))
            {
                continue;
            }

            int length = TextModel.Lines[line].Length;
            if (!_wrapLines || length <= width)
            {
                _rows.Add(new TextRow(line, 0, length));
                continue;
            }

            for (int start = 0; start < length; start += width)
            {
                _rows.Add(new TextRow(line, start, Math.Min(width, length - start)));
            }
        }

        _rowsKey = (TextModel.Lines.Count, width, _wrapLines, _foldVersion, _showLineNumbers);
        if (RowMode)
        {
            ScrollableModel.ScrollableSize = new Size(0, _rows.Count);
        }

        if (top is { } kept)
        {
            ScrollableModel.ScrollPosition.Y = RowOf(kept);
        }
    }

    /// <summary>The first row of the current layout that shows the text <paramref name="kept"/> starts with; the last row of its line when that text is gone.</summary>
    private int RowOf(TextRow kept)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (row.Line == kept.Line && kept.Start >= row.Start && kept.Start < row.Start + Math.Max(1, row.Length))
            {
                return i;
            }
        }

        int last = _rows.FindLastIndex(r => r.Line == kept.Line);
        return Math.Clamp(last < 0 ? 0 : last, 0, Math.Max(0, _rows.Count - 1));
    }

    /// <summary>The line and the column in it at the top of the screen (the top row, when wrapped).</summary>
    public (long Line, int Start) TopRow()
    {
        var rows = ScreenRows;
        if (rows.Count == 0)
        {
            return (0, 0);
        }

        var index = Math.Clamp((int)ScrollableModel.ScrollPosition.Y, 0, rows.Count - 1);
        return (rows[index].Line, rows[index].Start);
    }

    /// <summary>The screen row the cursor is on: its line and column within the line. At a row break the cursor goes to the row that starts there.</summary>
    internal int CursorScreenRow()
    {
        var rows = ScreenRows;
        long line = ScrollableModel.CursorPosition.Y;
        long column = ScrollableModel.CursorPosition.X;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Line == line && column >= row.Start && column < row.Start + row.Length)
            {
                return i;
            }
        }

        // The end of a line sits just past its last row's text.
        return Math.Max(0, rows.ToList().FindLastIndex(r => r.Line == line));
    }

    /// <summary>
    /// Moves the cursor <paramref name="delta"/> screen rows up or down, keeping its column within the row where it can.
    /// Only meaningful when the lines are wrapped: unwrapped, a screen row is a line and the caller moves by line.
    /// </summary>
    protected void MoveCursorByScreenRow(int delta)
    {
        if (!RowMode || TextModel.Lines.Count == 0 || ViewSize.IsEmpty)
        {
            return;
        }

        var rows = ScreenRows;
        int current = CursorScreenRow();
        int column = (int)ScrollableModel.CursorPosition.X - rows[current].Start;
        int target = Math.Clamp(current + delta, 0, rows.Count - 1);
        var row = rows[target];

        // A row that is cut by a wrap cannot hold the caret just past its text: that position is the next row's first column.
        bool endsLine = target + 1 >= rows.Count || rows[target + 1].Line != row.Line;
        int maxColumn = endsLine ? row.Length : Math.Max(0, row.Length - 1);
        ScrollableModel.CursorPosition.Y = row.Line;
        ScrollableModel.CursorPosition.X = row.Start + Math.Min(Math.Max(0, column), maxColumn);
        KeepRowVisible(target);
        StateHasChanged();
    }

    /// <summary>Scrolls, when wrapped, so that screen row <paramref name="row"/> is on the screen.</summary>
    private void KeepRowVisible(int row)
    {
        int visibleRows = Math.Max(1, (int)ViewSize.Height - 1);
        long top = ScrollableModel.ScrollPosition.Y;
        if (row < top)
        {
            top = row;
        }
        else if (row >= top + visibleRows)
        {
            top = row - visibleRows + 1;
        }

        ScrollableModel.ScrollPosition.Y = Math.Max(0, top);
    }

    /// <summary>
    /// Brings a line, and the column in it, onto the screen and puts the cursor there. The line lands a third of the way
    /// down the view, so the text before it stays in sight. This is how search hits and go-to-line show their place.
    /// </summary>
    public void ShowLine(long line, long column)
    {
        if (TextModel.Lines.Count == 0)
        {
            return;
        }

        long row = Math.Clamp(line, 0, TextModel.Lines.Count - 1);
        RevealLine((int)row);
        long col = Math.Clamp(column, 0, TextModel.Lines[(int)row].Length);
        ScrollableModel.CursorPosition.Y = row;
        ScrollableModel.CursorPosition.X = col;

        int visibleRows = Math.Max(1, (int)ViewSize.Height - 1);
        int visibleColumns = Math.Max(1, (int)ViewSize.Width - 2);
        long index;
        long total;
        if (RowMode)
        {
            IReadOnlyList<TextRow> rows = ScreenRows;
            total = rows.Count;
            int found = rows.ToList().FindIndex(r => r.Line == row && col >= r.Start && col < r.Start + r.Length);
            if (found < 0)
            {
                // The end of a line, which sits just past its last row's text.
                found = rows.ToList().FindLastIndex(r => r.Line == row);
            }

            index = Math.Max(0, found);
        }
        else
        {
            total = TextModel.Lines.Count;
            index = row;
            if (col < ScrollableModel.ScrollPosition.X || col >= ScrollableModel.ScrollPosition.X + visibleColumns)
            {
                ScrollableModel.ScrollPosition.X = Math.Max(0, col - visibleColumns / 3);
            }
        }

        long maxScroll = Math.Max(0, total - visibleRows);
        ScrollableModel.ScrollPosition.Y = Math.Clamp(index - visibleRows / 3, 0, maxScroll);
        StateHasChanged();
    }

    /// <summary>The number of the line the cursor is on, counting from 1.</summary>
    public long CursorLine => ClampedCursor().Row + 1;

    // ---- Selection (a text cursor only: the editor) ----

    /// <summary>Where the selection began; it runs from here to the cursor. Null when nothing is selected.</summary>
    private (long Row, long Column)? _selectionAnchor;

    /// <summary>True from a press in the text until the button is released: movement then selects.</summary>
    private bool _dragSelecting;

    /// <summary>Moves the cursor to the text under <paramref name="pointer"/> — or a step towards it, past an edge of the view — and extends the selection to it.</summary>
    private void ExtendSelectionTo(Position pointer)
    {
        var (cursorRow, cursorColumn) = ClampedCursor();
        long column = (long)pointer.X - (long)AbsolutePosition.X;
        long row = (long)pointer.Y - (long)AbsolutePosition.Y;
        var viewWidth = ViewSize.Width - 1;
        var viewHeight = ViewSize.Height - 1;

        long rowStart = 0;
        long newRow;
        if (RowMode && ScreenRows.Count > 0)
        {
            // Rows are not lines here: find the line under the pointer, and where in it the row starts.
            IReadOnlyList<TextRow> screenRows = ScreenRows;
            if (row >= 0 && row < viewHeight)
            {
                TextRow under = screenRows[(int)Math.Clamp((long)ScrollableModel.ScrollPosition.Y + row, 0, screenRows.Count - 1)];
                newRow = under.Line;
                rowStart = under.Start;
            }
            else
            {
                newRow = row < 0 ? cursorRow - 1 : cursorRow + 1;
            }
        }
        else
        {
            newRow = row < 0 ? cursorRow - 1
                : row >= viewHeight ? cursorRow + 1
                : (long)ScrollableModel.ScrollPosition.Y + row;
        }

        newRow = Math.Clamp(newRow, 0, TextModel.Lines.Count - 1);
        var newColumn = column < 0 ? cursorColumn - 1
            : column >= viewWidth ? cursorColumn + 1
            : RowMode ? rowStart + Math.Max(0, column - GutterWidth)
            : (long)ScrollableModel.ScrollPosition.X + column;
        newColumn = Math.Clamp(newColumn, 0, TextModel.Lines[(int)newRow].Length);

        ScrollableModel.CursorPosition.Y = newRow;
        ScrollableModel.CursorPosition.X = newColumn;
        UpdateSelectionModel();
        KeepCursorInText();
        StateHasChanged();
    }

    /// <summary>True while a Shift+movement key is being carried out as the plain movement, so it does not end the selection.</summary>
    private bool _extendingSelection;

    private (long Row, long Column) ClampedCursor()
    {
        if (TextModel.Lines.Count == 0)
        {
            return (0, 0);
        }

        var row = Math.Clamp(ScrollableModel.CursorPosition.Y, 0, TextModel.Lines.Count - 1);
        return (row, Math.Clamp(ScrollableModel.CursorPosition.X, 0, TextModel.Lines[(int)row].Length));
    }

    private (long Row, long Column) ClampedAnchor()
    {
        var anchor = _selectionAnchor ?? ClampedCursor();
        if (TextModel.Lines.Count == 0)
        {
            return (0, 0);
        }

        var row = Math.Clamp(anchor.Row, 0, TextModel.Lines.Count - 1);
        return (row, Math.Clamp(anchor.Column, 0, TextModel.Lines[(int)row].Length));
    }

    /// <summary>Whether any text is selected.</summary>
    public bool HasSelection => _selectionAnchor is not null && ClampedAnchor() != ClampedCursor();

    /// <summary>The first and the last position of the selection, in reading order; the end is just past the last selected character.</summary>
    public ((long Row, long Column) Start, (long Row, long Column) End) SelectionRange
    {
        get
        {
            var anchor = ClampedAnchor();
            var cursor = ClampedCursor();
            return anchor.CompareTo(cursor) <= 0 ? (anchor, cursor) : (cursor, anchor);
        }
    }

    /// <summary>The selected text, lines joined with a line feed; empty when nothing is selected.</summary>
    public string SelectedText
    {
        get
        {
            if (!HasSelection)
            {
                return string.Empty;
            }

            var ((startRow, startColumn), (endRow, endColumn)) = SelectionRange;
            if (startRow == endRow)
            {
                return TextModel.Lines[(int)startRow][(int)startColumn..(int)endColumn];
            }

            var parts = new List<string> { TextModel.Lines[(int)startRow][(int)startColumn..] };
            for (var row = startRow + 1; row < endRow; row++)
            {
                parts.Add(TextModel.Lines[(int)row]);
            }

            parts.Add(TextModel.Lines[(int)endRow][..(int)endColumn]);
            return string.Join('\n', parts);
        }
    }

    /// <summary>Selects the whole text, with the cursor at its end. Only an editor (a text cursor) selects.</summary>
    public void SelectAll()
    {
        if (!_isCursorEnabled || TextModel.Lines.Count == 0)
        {
            return;
        }

        _selectionAnchor = (0, 0);
        ScrollableModel.CursorPosition.Y = TextModel.Lines.Count - 1;
        ScrollableModel.CursorPosition.X = TextModel.Lines[TextModel.Lines.Count - 1].Length;
        UpdateSelectionModel();
        KeepCursorInText();
        StateHasChanged();
    }

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="cursor"/> and puts the cursor at the end. Only an editor (a text cursor) selects.</summary>
    protected void SelectRange((long Row, long Column) anchor, (long Row, long Column) cursor)
    {
        if (!_isCursorEnabled || TextModel.Lines.Count == 0)
        {
            return;
        }

        _selectionAnchor = anchor;
        ScrollableModel.CursorPosition.Y = cursor.Row;
        ScrollableModel.CursorPosition.X = cursor.Column;
        UpdateSelectionModel();
        KeepCursorInText();
        StateHasChanged();
    }

    protected void ClearSelection()
    {
        _selectionAnchor = null;
        SelectionTextModel.IsSelected = false;
        SelectionTextModel.Start = null;
        SelectionTextModel.End = null;
    }

    private void UpdateSelectionModel()
    {
        if (!HasSelection)
        {
            ClearSelection();
            return;
        }

        var ((startRow, startColumn), (endRow, endColumn)) = SelectionRange;
        SelectionTextModel.IsSelected = true;
        SelectionTextModel.Start = new Position(startColumn, startRow);
        SelectionTextModel.End = new Position(endColumn, endRow);
    }

    /// <summary>Removes the selected text and puts the cursor where it began. The editor calls this; the viewer has no cursor.</summary>
    protected void RemoveSelectedText()
    {
        if (!HasSelection)
        {
            return;
        }

        var ((startRow, startColumn), (endRow, endColumn)) = SelectionRange;
        var lines = TextModel.Lines;
        lines[(int)startRow] = lines[(int)startRow][..(int)startColumn] + lines[(int)endRow][(int)endColumn..];
        for (var row = endRow; row > startRow; row--)
        {
            lines.RemoveAt((int)row);
        }

        ClearSelection();
        ScrollableModel.CursorPosition.Y = startRow;
        ScrollableModel.CursorPosition.X = startColumn;
        TextModel.TriggerPropertyChanged();
        Refresh();
        KeepCursorInText();
    }

    private static bool IsSelectionMovementKey(ConsoleKey key) => key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow
        or ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Home or ConsoleKey.End
        or ConsoleKey.PageUp or ConsoleKey.PageDown;

    /// <summary>Colours the part of row <paramref name="row"/> that is selected, in a line drawn from the scroll position on.</summary>
    private void HighlightSelectedPart(ConxelString drawn, int row, int lineLength, ColorScheme colorScheme)
    {
        if (!HasSelection)
        {
            return;
        }

        var ((startRow, startColumn), (endRow, endColumn)) = SelectionRange;
        if (row < startRow || row > endRow)
        {
            return;
        }

        // A row the selection runs on past takes one more cell, which stands for its line break.
        var from = row == startRow ? startColumn : 0;
        var to = row == endRow ? endColumn : lineLength + 1;
        var scroll = ScrollableModel.ScrollPosition.X;
        var start = (int)Math.Max(0, from - scroll);
        var end = (int)(to - scroll);
        if (end > start)
        {
            drawn.HighlightRange(start, end, colorScheme.SelectedFocusedForeground, colorScheme.SelectedFocusedBackground);
        }
    }

    /// <summary>
    /// In an editor, Left at the start of a line goes to the end of the one above, Right at the end of a line to the
    /// start of the next, and End goes to the end of the line the cursor is on (the scrolling command it replaces used
    /// the first visible line). Everything else is the commands' to handle; afterwards the cursor is kept in the text.
    /// </summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Up/Down", "Scroll a line");
        yield return ("PageUp/PageDown", "Scroll a page");
        yield return ("Left/Right", "Scroll sideways");
        yield return ("Ctrl+Home/Ctrl+End", "Go to the start or the end of the file");
        if (_enableFolding)
        {
            yield return ("Alt+Left/Alt+Right", "Fold or unfold the region at the cursor");
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        SyncScrollableSize();
        if (HandleFoldKey(keyEvent))
        {
            return true;
        }

        // No IsFocused test: the editor never takes the focus itself (its dialog hands it the keys), and its own
        // commands act without it too.
        if (_isCursorEnabled && IsEnabled && TextModel.Lines.Count > 0 && !_extendingSelection
            && !keyEvent.Control && !keyEvent.Alt)
        {
            // Shift with a movement key moves as the plain key does, from an anchor, and the selection follows.
            if (keyEvent.Shift && IsSelectionMovementKey(keyEvent.ConsoleKey))
            {
                _selectionAnchor ??= ClampedCursor();
                _extendingSelection = true;
                try
                {
                    await HandleKeyEventAsync(new KeyEvent(keyEvent.KeyChar, keyEvent.ConsoleKey, false, false, false));
                }
                finally
                {
                    _extendingSelection = false;
                }

                UpdateSelectionModel();
                StateHasChanged();
                return true;
            }

            // With a selection up, Left and Right collapse it to its start and its end.
            if (!keyEvent.Shift && HasSelection && keyEvent.ConsoleKey is ConsoleKey.LeftArrow or ConsoleKey.RightArrow)
            {
                var (start, end) = SelectionRange;
                var target = keyEvent.ConsoleKey == ConsoleKey.LeftArrow ? start : end;
                ClearSelection();
                ScrollableModel.CursorPosition.Y = target.Row;
                ScrollableModel.CursorPosition.X = target.Column;
                KeepCursorInText();
                StateHasChanged();
                return true;
            }
        }

        if (_isCursorEnabled && IsEnabled && TextModel.Lines.Count > 0
            && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            var row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, TextModel.Lines.Count - 1);
            var length = TextModel.Lines[row].Length;
            var column = Math.Min(ScrollableModel.CursorPosition.X, length);
            var handled = true;
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.LeftArrow when column == 0 && row > 0:
                    ScrollableModel.CursorPosition.Y = row - 1;
                    ScrollableModel.CursorPosition.X = TextModel.Lines[row - 1].Length;
                    break;
                case ConsoleKey.RightArrow when column >= length && row + 1 < TextModel.Lines.Count:
                    ScrollableModel.CursorPosition.Y = row + 1;
                    ScrollableModel.CursorPosition.X = 0;
                    break;
                case ConsoleKey.End:
                    ScrollableModel.CursorPosition.Y = row;
                    ScrollableModel.CursorPosition.X = length;
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                if (!_extendingSelection)
                {
                    ClearSelection();
                }

                KeepCursorInText();
                StateHasChanged();
                return true;
            }

            ScrollableModel.CursorPosition.X = column;
        }

        var result = await base.HandleKeyEventAsync(keyEvent);
        if (result)
        {
            // Any key that acted other than by selecting ends the selection.
            if (!_extendingSelection)
            {
                ClearSelection();
            }

            KeepCursorInText();
        }

        return result;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        _verticalScrollBar.ScrollableModel = ScrollableModel;
        _horizontalScrollBar.ScrollableModel = ScrollableModel;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            if (TextModel.Lines.Count != 0)
            {
                ScrollableModel.ScrollableSize = new Size(
                    TextModel.Lines.Max(l => l.Length),
                    TextModel.Lines.Count);
            }
            else
            {
                ScrollableModel.ScrollableSize = new Size(0, 0);
            }
            if (!ViewSize.IsEmpty)
            {
                ScrollableModel.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), Math.Max(0, ViewSize.Height - 1));
            }
        }

        if (Parent != null && RowMode && !ViewSize.IsEmpty)
        {
            RebuildRows(TextAreaWidth);
        }

        Layout.Center(this);

        if (!ViewSize.IsEmpty)
        {
            _verticalScrollBar.Position = new Position(Math.Max(0, ViewSize.Width - 1), 0);
            _verticalScrollBar.ViewSize = new Size(1, Math.Max(0, ViewSize.Height - 1));
            _horizontalScrollBar.Position = new Position(0, Math.Max(0, ViewSize.Height - 1));
            _horizontalScrollBar.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);
        }
    }

    /// <summary>Rows the wheel moves the view by in one notch.</summary>
    private const int WheelRows = 3;

    /// <summary>The wheel scrolls the text three rows; the scroll bars handle their own clicks.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        // Dragging from a press in the text selects: the cursor follows the pointer, and past an edge of the view it
        // moves a step with each movement, so the view scrolls and a selection can run beyond what shows.
        if (mouseEvent.IsDrag && _isCursorEnabled && _dragSelecting && IsEnabled && TextModel.Lines.Count > 0)
        {
            ExtendSelectionTo(mouseEvent.Position);
            return true;
        }

        if (mouseEvent.IsRelease)
        {
            _dragSelecting = false;
        }

        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (isHandled || TextModel.Lines.Count == 0 || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        // A click in the text moves the cursor there: the highlighted row always, and — in an editor,
        // where the cursor is a caret — the column too, clamped to the end of the line. The scroll bars
        // (the last column and the last row) took their own clicks above.
        if (mouseEvent.LeftClicked && mouseEvent.WheelDelta == 0)
        {
            ClearSelection();
            long column = (long)mouseEvent.Position.X - (long)AbsolutePosition.X;
            long row = (long)mouseEvent.Position.Y - (long)AbsolutePosition.Y;
            if (column >= ViewSize.Width - 1 || row >= ViewSize.Height - 1)
            {
                return false;
            }

            if (!IsFocused)
            {
                SetFocus();
            }

            long line = Math.Clamp((long)ScrollableModel.ScrollPosition.Y + row, 0, TextModel.Lines.Count - 1);
            long lineStart = 0;
            if (RowMode && ScreenRows.Count > 0)
            {
                // A wrapped row is part of a line: the click lands on that line, at its place in the row.
                TextRow clicked = ScreenRows[(int)Math.Clamp((long)ScrollableModel.ScrollPosition.Y + row, 0, ScreenRows.Count - 1)];
                line = clicked.Line;
                lineStart = clicked.Start;
                if (_enableFolding && column < GutterWidth && lineStart == 0 && CanFold((int)line))
                {
                    // A click on the gutter's marker opens or closes the region.
                    ToggleFold((int)line);
                    return true;
                }
            }

            ScrollableModel.CursorPosition.Y = RowMode ? ScrollableModel.ScrollPosition.Y + row : line;
            if (_isCursorEnabled)
            {
                ScrollableModel.CursorPosition.Y = line;
                ScrollableModel.CursorPosition.X = Math.Clamp(lineStart + (RowMode ? -GutterWidth : (long)ScrollableModel.ScrollPosition.X) + column, 0, TextModel.Lines[(int)line].Length);
                _selectionAnchor = ClampedCursor();
                _dragSelecting = true;
            }

            StateHasChanged();
            return true;
        }

        if (mouseEvent.WheelDelta == 0)
        {
            return false;
        }

        // The wheel moves the view, not the cursor: the cursor goes along only when the view would leave it behind.
        SyncScrollableSize();
        long visibleRows = Math.Max(1, ScrollableModel.ViewSize.Height);
        long totalRows = RowMode ? ScreenRows.Count : TextModel.Lines.Count;
        long maxScroll = Math.Max(0, totalRows - visibleRows);
        long scroll = Math.Clamp(ScrollableModel.ScrollPosition.Y + (mouseEvent.WheelDelta > 0 ? -WheelRows : WheelRows), 0, maxScroll);
        ScrollableModel.ScrollPosition.Y = scroll;
        if (!RowMode)
        {
            ScrollableModel.CursorPosition.Y = Math.Clamp(ScrollableModel.CursorPosition.Y, scroll, Math.Min(scroll + visibleRows - 1, TextModel.Lines.Count - 1));
        }

        StateHasChanged();
        return true;
    }

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return; // TODO
        }

        if (RowMode)
        {
            // The rows are laid out from the text, so an edit since the last frame has to reach them first.
            SyncScrollableSize();
            RefreshRowsIfTextChanged();
            DrawWrapped(buffer, colorScheme);
            return;
        }

        SyncScrollableSize();
        Color foregroundColor;
        Color backgroundColor;
        var size = new Size(Math.Max(0, ViewSize.Width - 2), 1);
        bool inComment = StartsInBlockComment((int)Math.Min(ScrollableModel.ScrollPosition.Y, TextModel.Lines.Count));
        for (int i = 0; i < ViewSize.Height - 1; i++)
        {
            var indexY = ScrollableModel.ScrollPosition.Y + i;
            if (indexY < TextModel.Lines.Count)
            {
                if (ScrollableModel.CursorPosition.Y == indexY)
                {
                    foregroundColor = colorScheme.SelectedFocusedForeground;
                    backgroundColor = Color.Yellow;
                }
                else
                {
                    foregroundColor = Color.White;
                    backgroundColor = colorScheme.Background;
                }
                var line = TextModel.Lines[(int)indexY];
                var positionX = Math.Min((int)ScrollableModel.ScrollPosition.X, line.Length);
                line = line[positionX..]
                    .PadRight(Math.Max(0, (int)ViewSize.Width - 2));
                var conxelLine = new ConxelString(line, foregroundColor, backgroundColor);
                var highlightPositionX = (int)(positionX - ScrollableModel.ScrollPosition.X);
                bool isCursorLine = ScrollableModel.CursorPosition.Y == indexY;
                // The cursor's row keeps the cursor colours; the others are coloured by what the text is.
                ColorizeSyntax(conxelLine, TextModel.Lines[(int)indexY], inComment, positionX, out inComment, isCursorLine);
                DecorateLine(conxelLine, (int)indexY, isCursorLine, colorScheme);
                DecorateText(conxelLine, (int)indexY, positionX, isCursorLine, colorScheme);
                HighlightSearchResults(conxelLine, highlightPositionX, (int)indexY);
                HighlightSelectedPart(conxelLine, (int)indexY, TextModel.Lines[(int)indexY].Length, colorScheme);
                MarkWhitespace(conxelLine, TextModel.Lines[(int)indexY], positionX, int.MaxValue, colorScheme);
                buffer.Write(
                    ZOrder,
                    new Position(AbsolutePosition.X, (uint)(AbsolutePosition.Y + i)),
                    size,
                    conxelLine
                    );
            }
            else
            {
                // Blank the rows past the end of the text: a layer is not cleared between frames.
                buffer.Write(
                    ZOrder,
                    new Position(AbsolutePosition.X, (uint)(AbsolutePosition.Y + i)),
                    size,
                    new ConxelString(string.Empty.PadRight((int)size.Width), Color.White, colorScheme.Background));
            }
        }

        _verticalScrollBar.Draw(buffer);
        _horizontalScrollBar.Draw(buffer);
    }

    /// <summary>The wrapped view: one screen row for each row of the scroll position, each showing its part of a line.</summary>
    private void DrawWrapped(IBuffer buffer, ColorScheme colorScheme)
    {
        int gutter = GutterWidth;
        int width = Math.Max(0, (int)ViewSize.Width - 2 - gutter);
        var size = new Size(width + gutter, 1);
        IReadOnlyList<TextRow> rows = ScreenRows;

        // A line is tokenized once, however many rows it wraps to; the next line starts in a block comment if this one ended in one.
        int tokenizedLine = -1;
        bool nextStartsInComment = false;
        List<SyntaxSpan> lineSpans = [];
        for (int i = 0; i < ViewSize.Height - 1; i++)
        {
            var index = ScrollableModel.ScrollPosition.Y + i;
            if (index < rows.Count)
            {
                TextRow row = rows[(int)index];
                string line = TextModel.Lines[row.Line];
                int start = Math.Min(row.Start, line.Length);
                string text = line.Substring(start, Math.Min(row.Length, line.Length - start));
                int foldNote = -1;
                bool endsLine = start + row.Length >= line.Length;
                if (endsLine && IsFolded(row.Line) && FoldRegion(row.Line) is { } folded)
                {
                    foldNote = text.Length;
                    text += $" ⋯ {folded.Last - folded.First + 1} lines";
                }

                text = text.Length > width ? text[..width] : text.PadRight(width);
                bool cursorLine = ScrollableModel.CursorPosition.Y == row.Line;
                var conxelLine = new ConxelString(
                    text,
                    cursorLine ? colorScheme.SelectedFocusedForeground : Color.White,
                    cursorLine ? Color.Yellow : colorScheme.Background);
                if (_syntax is not null)
                {
                    if (row.Line != tokenizedLine)
                    {
                        bool startsInComment = tokenizedLine < 0 ? StartsInBlockComment(row.Line) : nextStartsInComment;
                        lineSpans.Clear();
                        nextStartsInComment = Highlighting.Tokenize(_syntax, line, startsInComment, lineSpans);
                        tokenizedLine = row.Line;
                    }

                    // The cursor's row keeps the cursor colours, as in the unwrapped view.
                    if (!cursorLine)
                    {
                        ApplySpans(conxelLine, lineSpans, start);
                    }
                }

                HighlightSearchHitsInRow(conxelLine, row);
                DecorateText(conxelLine, row.Line, start, cursorLine, colorScheme);
                MarkWhitespace(conxelLine, line, start, row.Length, colorScheme);
                if (foldNote >= 0 && foldNote < width)
                {
                    conxelLine.HighlightRange(foldNote, width, colorScheme.DisabledForeground, colorScheme.Background);
                }

                if (gutter > 0)
                {
                    var gutterLine = new ConxelString(GutterText(row), cursorLine ? colorScheme.Foreground : colorScheme.DisabledForeground, colorScheme.Background);
                    gutterLine.Append(conxelLine);
                    conxelLine = gutterLine;
                }

                buffer.Write(
                    ZOrder,
                    new Position(AbsolutePosition.X, (uint)(AbsolutePosition.Y + i)),
                    size,
                    conxelLine);
            }
            else
            {
                buffer.Write(
                    ZOrder,
                    new Position(AbsolutePosition.X, (uint)(AbsolutePosition.Y + i)),
                    size,
                    new ConxelString(string.Empty.PadRight(width + gutter), Color.White, colorScheme.Background));
            }
        }

        _verticalScrollBar.Draw(buffer);
        _horizontalScrollBar.Draw(buffer);
    }

    /// <summary>Highlights the hits that fall in a wrapped row: a hit that crosses the row's edge is cut where it is cut.</summary>
    private void HighlightSearchHitsInRow(ConxelString conxelLine, TextRow row)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        foreach (var hit in SearchModel.Hits.AsSnapshot().Where(h => h.Y == row.Line))
        {
            var searchLength = SearchModel.LengthAt(hit);
            long from = Math.Max(hit.X, row.Start);
            long to = Math.Min(hit.X + searchLength, row.Start + row.Length);
            if (to > from)
            {
                conxelLine.HighlightRange((int)(from - row.Start), (int)(to - row.Start), colorScheme.SelectedForeground, colorScheme.SelectedFocusedBackground);
            }
        }
    }

    private void HighlightSearchResults(ConxelString conxelLine, int x, int y)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var matchingLines = SearchModel.Hits.AsSnapshot().Where(h => h.Y == y);
        foreach (var hit in matchingLines)
        {
            var searchLength = SearchModel.LengthAt(hit);
            conxelLine.HighlightRange((int)hit.X, (int)hit.X + searchLength, colorScheme.SelectedForeground, colorScheme.SelectedFocusedBackground);
        }
    }
}
