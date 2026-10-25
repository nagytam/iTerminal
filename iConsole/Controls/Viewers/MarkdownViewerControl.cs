using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Markdown;
using iConsole.Services;

namespace iConsole.Controls.Viewers;

/// <summary>
/// A read-only viewer for Markdown text. The document is parsed when it is set and laid out for the control's width when it
/// is drawn, so resizing re-wraps it. It scrolls with the arrow keys, Page Up and Page Down, Home and End, and the wheel.
///
/// <para>Like every input control it acts only when it has focus and is enabled. It draws every row of its rectangle, padded,
/// because nothing clears a layer between frames.</para>
/// </summary>
public class MarkdownViewerControl : Control
{
    private IReadOnlyList<MarkdownBlock> _blocks = [];
    private IReadOnlyList<MarkdownLine> _lines = [];
    private int _linesWidth = -1;
    private string _markdown = string.Empty;

    public MarkdownViewerControl(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
    }

    /// <summary>The Markdown source. Setting it parses the text and returns the view to the top.</summary>
    public string Markdown
    {
        get => _markdown;
        set
        {
            _markdown = value ?? string.Empty;
            _blocks = MarkdownParser.Parse(_markdown);
            _linesWidth = -1;
            ScrollOffset = 0;
            StateHasChanged();
        }
    }

    /// <summary>The first laid-out row that is on screen.</summary>
    public int ScrollOffset { get; private set; }

    /// <summary>The document as laid out for the current width.</summary>
    public IReadOnlyList<MarkdownLine> Lines
    {
        get
        {
            EnsureLines();
            return _lines;
        }
    }

    /// <summary>Scrolls by <paramref name="rows"/> rows (negative is up), keeping the view within the document.</summary>
    public void ScrollBy(int rows)
    {
        EnsureLines();
        ScrollTo(ScrollOffset + rows);
    }

    private void ScrollTo(int row)
    {
        EnsureLines();
        var lastFirstRow = Math.Max(0, _lines.Count - (int)ViewSize.Height);
        var clamped = Math.Clamp(row, 0, lastFirstRow);
        if (clamped != ScrollOffset)
        {
            ScrollOffset = clamped;
            StateHasChanged();
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (IsEnabled && IsFocused && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            var page = Math.Max(1, (int)ViewSize.Height - 1);
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    ScrollBy(-1);
                    return true;
                case ConsoleKey.DownArrow:
                    ScrollBy(1);
                    return true;
                case ConsoleKey.PageUp:
                    ScrollBy(-page);
                    return true;
                case ConsoleKey.PageDown:
                    ScrollBy(page);
                    return true;
                case ConsoleKey.Home:
                    ScrollTo(0);
                    return true;
                case ConsoleKey.End:
                    ScrollTo(int.MaxValue);
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (IsEnabled && mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            ScrollBy(-mouseEvent.WheelDelta * 3);
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        EnsureLines();
        var width = (int)ViewSize.Width;
        for (var row = 0; row < (int)ViewSize.Height; row++)
        {
            var index = ScrollOffset + row;
            var x = 0;
            if (index < _lines.Count)
            {
                foreach (var span in _lines[index].Spans)
                {
                    if (span.Text.Length == 0 || x >= width)
                    {
                        continue;
                    }

                    var text = span.Text.Length > width - x ? span.Text[..(width - x)] : span.Text;
                    WriteSpan(buffer, row, x, text, _lines[index].Kind, span.Style);
                    x += text.Length;
                }
            }

            // Pad the rest of the row so a shorter line does not leave the tail of the one before it on screen.
            if (x < width)
            {
                WriteSpan(buffer, row, x, new string(' ', width - x), MarkdownBlockKind.Paragraph, MarkdownStyle.Plain);
            }
        }
    }

    private void WriteSpan(IBuffer buffer, int row, int x, string text, MarkdownBlockKind kind, MarkdownStyle style)
    {
        var scheme = ThemeService.CurrentColorScheme;
        var foreground = scheme.Foreground;
        if ((style & MarkdownStyle.Code) != 0 || kind == MarkdownBlockKind.Code)
        {
            foreground = scheme.DisabledSelectedForeground;
        }
        else if (kind is MarkdownBlockKind.Quote or MarkdownBlockKind.Rule)
        {
            foreground = scheme.DisabledForeground;
        }

        var bold = (style & MarkdownStyle.Bold) != 0 || kind == MarkdownBlockKind.Heading;
        var italic = (style & MarkdownStyle.Italic) != 0 || kind == MarkdownBlockKind.Quote;
        var underline = (style & MarkdownStyle.Link) != 0;

        var conxel = new ConxelString(text, foreground, scheme.Background, bold, italic, underline, false);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X + x, AbsolutePosition.Y + row), new Size(text.Length, 1), conxel);
    }

    private void EnsureLines()
    {
        var width = (int)ViewSize.Width;
        if (width == _linesWidth)
        {
            return;
        }

        _lines = MarkdownLayout.Wrap(_blocks, width);
        _linesWidth = width;
        ScrollOffset = Math.Clamp(ScrollOffset, 0, Math.Max(0, _lines.Count - (int)ViewSize.Height));
    }
}
