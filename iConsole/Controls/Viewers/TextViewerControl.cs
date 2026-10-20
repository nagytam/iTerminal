using iConsole.Buffers;
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
        CommandHelper.AddVerticalScrollingCommands(this, ScrollableModel, false);
        CommandHelper.AddHorizontalScrollingCommands(this, ScrollableModel, TextModel, SelectionTextModel, isCursorEnabled);
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

    /// <summary>
    /// In an editor, Left at the start of a line goes to the end of the one above, Right at the end of a line to the
    /// start of the next, and End goes to the end of the line the cursor is on (the scrolling command it replaces used
    /// the first visible line). Everything else is the commands' to handle; afterwards the cursor is kept in the text.
    /// </summary>
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // No IsFocused test: the editor never takes the focus itself (its dialog hands it the keys), and its own
        // commands act without it too.
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
                KeepCursorInText();
                StateHasChanged();
                return true;
            }

            ScrollableModel.CursorPosition.X = column;
        }

        var result = await base.HandleKeyEventAsync(keyEvent);
        if (result)
        {
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
                ScrollableModel.ViewSize = new Size(ViewSize.Width - 1, ViewSize.Height - 1);
            }
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
            ScrollableModel.CursorPosition.Y = line;
            if (_isCursorEnabled)
            {
                ScrollableModel.CursorPosition.X = Math.Clamp((long)ScrollableModel.ScrollPosition.X + column, 0, TextModel.Lines[(int)line].Length);
            }

            StateHasChanged();
            return true;
        }

        if (mouseEvent.WheelDelta == 0)
        {
            return false;
        }

        long target = ScrollableModel.CursorPosition.Y + (mouseEvent.WheelDelta > 0 ? -WheelRows : WheelRows);
        ScrollableModel.CursorPosition.Y = Math.Clamp(target, 0, TextModel.Lines.Count - 1);
        ScrollableModel.ScrollToVisible();
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

        Color foregroundColor;
        Color backgroundColor;
        var size = new Size(Math.Max(0, ViewSize.Width - 2), 1);
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
                HighlightSearchResults(conxelLine, highlightPositionX, (int)indexY);
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
