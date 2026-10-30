using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Markdown;
using iConsole.Helper;
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

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled || _markdown.Length == 0)
        {
            yield break;
        }

        yield return ("Up/Down", UiText.Get("Hint.ScrollOneLine", "Scroll one line"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.ScrollPage", "Scroll a page"));
        yield return ("Home/End", UiText.Get("Hint.GoTopOrEnd", "Go to the top or the end"));
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

        // A click takes the focus, as on the other controls, so the arrow keys scroll the document the user clicked.
        if (IsEnabled && mouseEvent.LeftClicked && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            if (!IsFocused)
            {
                SetFocus();
            }

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
            if (index < _lines.Count && TryDrawHighlightedCode(buffer, row, index, width))
            {
                continue;
            }

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

    /// <summary>Where code fences get their language and colours from; the shared service unless one is given.</summary>
    public ISyntaxHighlightingService Highlighting { get; set; } = SyntaxHighlightingService.Default;

    /// <summary>How far back a block comment is looked for when a code line is drawn.</summary>
    private const int CodeCommentLookBack = 200;

    /// <summary>
    /// Draws a line of a fenced code block in the colours of its language, when the fence named one that is known.
    /// A block comment that began on an earlier line of the same block is followed. Returns false for any other line.
    /// </summary>
    private bool TryDrawHighlightedCode(IBuffer buffer, int row, int index, int width)
    {
        MarkdownLine line = _lines[index];
        if (line.Kind != MarkdownBlockKind.Code || line.Language.Length == 0
            || Highlighting.FindByName(line.Language) is not { } language)
        {
            return false;
        }

        var spans = new List<SyntaxSpan>();
        bool inComment = false;
        if (language.BlockComment is not null)
        {
            // Only the lines of this block count: a fence resets the state.
            int first = index;
            while (first > 0 && index - first < CodeCommentLookBack
                && _lines[first - 1].Kind == MarkdownBlockKind.Code && _lines[first - 1].Language == line.Language)
            {
                first--;
            }

            for (int i = first; i < index; i++)
            {
                spans.Clear();
                inComment = Highlighting.Tokenize(language, string.Concat(_lines[i].Spans.Select(s => s.Text)), inComment, spans);
            }
        }

        string text = string.Concat(line.Spans.Select(s => s.Text));
        if (text.Length > width)
        {
            text = text[..width];
        }

        spans.Clear();
        Highlighting.Tokenize(language, text, inComment, spans);
        var scheme = ThemeService.CurrentColorScheme;
        Color plain = scheme.DisabledSelectedForeground;
        var drawn = new ConxelString(text.PadRight(width), plain, scheme.Background);
        foreach (SyntaxSpan span in spans)
        {
            Color color = Highlighting.Palette.ColorOf(span.Kind, plain);
            for (int x = span.Start; x < Math.Min(width, span.Start + span.Length); x++)
            {
                Conxel cell = drawn.String[x];
                cell.ForegroundColor = color;
                drawn.String[x] = cell;
            }
        }

        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(width, 1), drawn);
        return true;
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
