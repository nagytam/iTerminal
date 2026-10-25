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

public class TextViewerControl : Control
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
                cursorPosition = new Position(
                    ScrollableModel.CursorPosition.X - ScrollableModel.ScrollPosition.X + AbsolutePosition.X,
                    ScrollableModel.CursorPosition.Y - ScrollableModel.ScrollPosition.Y + AbsolutePosition.Y
                    );
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
        TextModel.PropertyChanged += (_, _) => _scrollableSizeStale = true;
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
        if (_wrapLines && !ViewSize.IsEmpty)
        {
            _rowsKey = (-1, -1, false);
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
            inComment = SyntaxHighlighter.Tokenize(_syntax, TextModel.Lines[i], inComment, _spans);
        }

        return inComment;
    }

    private static Color ColorOf(SyntaxKind kind) => kind switch
    {
        SyntaxKind.Keyword => Color.Cyan,
        SyntaxKind.String => Color.Green,
        SyntaxKind.Comment => Color.Gray,
        SyntaxKind.Number => Color.Magenta,
        _ => Color.White,
    };

    /// <summary>Colours the keywords, strings, comments and numbers of line <paramref name="line"/> in a row drawn from the scroll position on.</summary>
    private void ColorizeSyntax(ConxelString drawn, string text, bool startsInComment, int scrollX, out bool endsInComment, bool skipColours)
    {
        endsInComment = false;
        if (_syntax is null)
        {
            return;
        }

        _spans.Clear();
        endsInComment = SyntaxHighlighter.Tokenize(_syntax, text, startsInComment, _spans);
        if (skipColours)
        {
            return;
        }

        ApplySpans(drawn, _spans, scrollX);
    }

    /// <summary>Colours the cells of <paramref name="drawn"/>, which starts at character <paramref name="offset"/> of its line, by what each span is.</summary>
    private static void ApplySpans(ConxelString drawn, List<SyntaxSpan> spans, int offset)
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

        var row = Math.Clamp(ScrollableModel.CursorPosition.Y, 0, TextModel.Lines.Count - 1);
        ScrollableModel.CursorPosition.Y = row;
        ScrollableModel.CursorPosition.X = Math.Clamp(ScrollableModel.CursorPosition.X, 0, TextModel.Lines[(int)row].Length);
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
            _rowsKey = (-1, -1, false);
            StateHasChanged();
        }
    }

    /// <summary>One screen row: the line it shows, and the part of that line it shows.</summary>
    internal readonly record struct TextRow(int Line, int Start, int Length);

    private List<TextRow> _rows = [];
    private (int Lines, int Width, bool Wrap) _rowsKey = (-1, -1, false);

    /// <summary>The screen rows for the current text and width. Rebuilt when either changes.</summary>
    internal IReadOnlyList<TextRow> ScreenRows
    {
        get
        {
            int width = Math.Max(1, (int)ViewSize.Width - 2);
            if (_rowsKey != (TextModel.Lines.Count, width, _wrapLines))
            {
                RebuildRows(width);
            }

            return _rows;
        }
    }

    private void RebuildRows(int width)
    {
        _rows = new List<TextRow>(TextModel.Lines.Count);
        for (int line = 0; line < TextModel.Lines.Count; line++)
        {
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

        _rowsKey = (TextModel.Lines.Count, width, _wrapLines);
        if (_wrapLines)
        {
            ScrollableModel.ScrollableSize = new Size(0, _rows.Count);
        }
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
        long col = Math.Clamp(column, 0, TextModel.Lines[(int)row].Length);
        ScrollableModel.CursorPosition.Y = row;
        ScrollableModel.CursorPosition.X = col;

        int visibleRows = Math.Max(1, (int)ViewSize.Height - 1);
        int visibleColumns = Math.Max(1, (int)ViewSize.Width - 2);
        long index;
        long total;
        if (_wrapLines)
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

        var newRow = row < 0 ? cursorRow - 1
            : row >= viewHeight ? cursorRow + 1
            : (long)ScrollableModel.ScrollPosition.Y + row;
        newRow = Math.Clamp(newRow, 0, TextModel.Lines.Count - 1);
        var newColumn = column < 0 ? cursorColumn - 1
            : column >= viewWidth ? cursorColumn + 1
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
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        SyncScrollableSize();
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

        if (Parent != null && _wrapLines && !ViewSize.IsEmpty)
        {
            RebuildRows(Math.Max(1, (int)ViewSize.Width - 2));
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
            if (_wrapLines && ScreenRows.Count > 0)
            {
                // A wrapped row is part of a line: the click lands on that line, at its place in the row.
                TextRow clicked = ScreenRows[(int)Math.Clamp((long)ScrollableModel.ScrollPosition.Y + row, 0, ScreenRows.Count - 1)];
                line = clicked.Line;
                lineStart = clicked.Start;
            }

            ScrollableModel.CursorPosition.Y = _wrapLines ? ScrollableModel.ScrollPosition.Y + row : line;
            if (_isCursorEnabled)
            {
                ScrollableModel.CursorPosition.Y = line;
                ScrollableModel.CursorPosition.X = Math.Clamp(lineStart + (_wrapLines ? 0 : (long)ScrollableModel.ScrollPosition.X) + column, 0, TextModel.Lines[(int)line].Length);
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
        long totalRows = _wrapLines ? ScreenRows.Count : TextModel.Lines.Count;
        long maxScroll = Math.Max(0, totalRows - visibleRows);
        long scroll = Math.Clamp(ScrollableModel.ScrollPosition.Y + (mouseEvent.WheelDelta > 0 ? -WheelRows : WheelRows), 0, maxScroll);
        ScrollableModel.ScrollPosition.Y = scroll;
        if (!_wrapLines)
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

        if (_wrapLines)
        {
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
                HighlightSearchResults(conxelLine, highlightPositionX, (int)indexY);
                HighlightSelectedPart(conxelLine, (int)indexY, TextModel.Lines[(int)indexY].Length, colorScheme);
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
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        var size = new Size(width, 1);
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
                string text = line.Substring(start, Math.Min(row.Length, line.Length - start)).PadRight(width);
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
                        nextStartsInComment = SyntaxHighlighter.Tokenize(_syntax, line, startsInComment, lineSpans);
                        tokenizedLine = row.Line;
                    }

                    // The cursor's row keeps the cursor colours, as in the unwrapped view.
                    if (!cursorLine)
                    {
                        ApplySpans(conxelLine, lineSpans, start);
                    }
                }

                HighlightSearchHitsInRow(conxelLine, row);
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
                    new ConxelString(string.Empty.PadRight(width), Color.White, colorScheme.Background));
            }
        }

        _verticalScrollBar.Draw(buffer);
        _horizontalScrollBar.Draw(buffer);
    }

    /// <summary>Highlights the hits that fall in a wrapped row: a hit that crosses the row's edge is cut where it is cut.</summary>
    private void HighlightSearchHitsInRow(ConxelString conxelLine, TextRow row)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var searchLength = SearchModel.Search.Length;
        foreach (var hit in SearchModel.Hits.AsSnapshot().Where(h => h.Y == row.Line))
        {
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
        var searchLength = SearchModel.Search.Length;
        var matchingLines = SearchModel.Hits.AsSnapshot().Where(h => h.Y == y);
        foreach (var hit in matchingLines)
        {
            conxelLine.HighlightRange((int)hit.X, (int)hit.X + searchLength, colorScheme.SelectedForeground, colorScheme.SelectedFocusedBackground);
        }
    }
}
