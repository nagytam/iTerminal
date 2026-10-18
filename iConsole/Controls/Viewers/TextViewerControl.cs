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
        ScrollableModel.PropertyChanged += OnPropertyChanged;
        _verticalScrollBar = new ScrollBar.ScrollBar(themeService) { ScrollBarOrientation = Orientation.Vertical };
        _horizontalScrollBar = new ScrollBar.ScrollBar(themeService) { ScrollBarOrientation = Orientation.Horizontal };
        IsCursorVisible = isCursorEnabled;

        AddControl(_verticalScrollBar);
        AddControl(_horizontalScrollBar);
        CommandHelper.AddVerticalScrollingCommands(this, ScrollableModel, false);
        CommandHelper.AddHorizontalScrollingCommands(this, ScrollableModel, TextModel, SelectionTextModel, isCursorEnabled);
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
            _verticalScrollBar.Position = new Position(ViewSize.Width - 1, 0);
            _verticalScrollBar.ViewSize = new Size(1, ViewSize.Height - 1);
            _horizontalScrollBar.Position = new Position(0, ViewSize.Height - 1);
            _horizontalScrollBar.ViewSize = new Size(ViewSize.Width - 2, 1);
        }
    }

    /// <summary>Rows the wheel moves the view by in one notch.</summary>
    private const int WheelRows = 3;

    /// <summary>The wheel scrolls the text three rows; the scroll bars handle their own clicks.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (isHandled || mouseEvent.WheelDelta == 0 || TextModel.Lines.Count == 0 || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
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
        var size = new Size(ViewSize.Width - 2, 1);
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
                    .PadRight((int)ViewSize.Width - 2);
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
