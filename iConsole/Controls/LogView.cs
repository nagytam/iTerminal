using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>How serious a <see cref="LogView"/> line is; it sets the line's colour and what <see cref="LogView.MinimumLevel"/> hides.</summary>
public enum LogEntryLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>One line held by a <see cref="LogView"/>.</summary>
public sealed record LogEntry(string Text, LogEntryLevel Level, IReadOnlyList<LogSpan>? Spans = null);

/// <summary>
/// Shows lines that a running task appends: a transfer, a git or docker command, a build. <see cref="Append"/> may be
/// called from any thread; the view repaints from <see cref="OnIdle"/>, so a worker never touches the control tree.
/// While <see cref="Follow"/> is on the view stays on the newest line; scrolling up turns it off (the count of lines
/// that arrived meanwhile is <see cref="UnreadCount"/>, shown at the bottom right), and End or scrolling to the bottom
/// turns it on again. At most <see cref="MaxLines"/> lines are kept, the oldest are dropped first, and a view that is
/// not following does not move when they go. <see cref="MinimumLevel"/> and <see cref="FilterText"/> hide lines without
/// deleting them. Lines are cut at the right edge; Left and Right scroll sideways.
/// </summary>
public class LogView : Control
{
    private const int WheelRows = 3;
    private const int ColumnStep = 8;

    private readonly Lock _gate = new();
    private readonly List<LogEntry> _entries = [];
    private readonly List<LogEntry> _visible = [];
    private int _maxLines = 10_000;
    private IReadOnlyList<LogHighlighter> _highlighters = LogHighlighters.Default;
    private bool _highlightingEnabled = true;
    private bool _parseAnsi = true;
    private LogEntryLevel _minimumLevel = LogEntryLevel.Trace;
    private string _filterText = string.Empty;
    private bool _follow = true;
    private int _top;
    private int _left;
    private int _unread;
    private int _version;
    private int _drawnVersion = -1;

    public LogView(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(40, 10);
    }

    /// <summary>The most lines kept; older ones are dropped as new ones arrive. At least 1.</summary>
    public int MaxLines
    {
        get { lock (_gate) { return _maxLines; } }
        set
        {
            lock (_gate)
            {
                _maxLines = Math.Max(1, value);
                Trim();
                _version++;
            }
        }
    }

    /// <summary>
    /// What colours URLs, paths, numbers, timestamps and error words in a line, from <see cref="LogHighlighters.Default"/> unless set.
    /// A change applies to lines added afterwards.
    /// </summary>
    public IReadOnlyList<LogHighlighter> Highlighters
    {
        get { lock (_gate) { return _highlighters; } }
        set { lock (_gate) { _highlighters = value ?? []; } }
    }

    /// <summary>Switches the highlighters off (the lines keep the colour of their level).</summary>
    public bool HighlightingEnabled
    {
        get { lock (_gate) { return _highlightingEnabled; } }
        set { lock (_gate) { _highlightingEnabled = value; } }
    }

    /// <summary>
    /// Reads ANSI colour codes in appended text (build tools, git and docker write them): colours and styles show, and every other
    /// escape sequence is dropped. With it off the escape characters are removed and the text is plain.
    /// </summary>
    public bool ParseAnsi
    {
        get { lock (_gate) { return _parseAnsi; } }
        set { lock (_gate) { _parseAnsi = value; } }
    }

    /// <summary>Lines below this level are hidden.</summary>
    public LogEntryLevel MinimumLevel
    {
        get { lock (_gate) { return _minimumLevel; } }
        set
        {
            lock (_gate)
            {
                if (_minimumLevel != value)
                {
                    _minimumLevel = value;
                    RebuildVisible();
                }
            }
        }
    }

    /// <summary>Only lines containing this text (ignoring case) are shown; empty shows all.</summary>
    public string FilterText
    {
        get { lock (_gate) { return _filterText; } }
        set
        {
            lock (_gate)
            {
                string text = value ?? string.Empty;
                if (_filterText != text)
                {
                    _filterText = text;
                    RebuildVisible();
                }
            }
        }
    }

    /// <summary>True while the view stays on the newest line.</summary>
    public bool Follow
    {
        get { lock (_gate) { return _follow; } }
        set
        {
            lock (_gate)
            {
                _follow = value;
                if (value)
                {
                    _unread = 0;
                }

                _version++;
            }
        }
    }

    /// <summary>Lines shown that arrived while the view was not following.</summary>
    public int UnreadCount
    {
        get { lock (_gate) { return _unread; } }
    }

    /// <summary>All lines held, whatever the filter.</summary>
    public int Count
    {
        get { lock (_gate) { return _entries.Count; } }
    }

    /// <summary>The lines now shown, after the level and text filters.</summary>
    public IReadOnlyList<LogEntry> VisibleEntries
    {
        get { lock (_gate) { return [.. _visible]; } }
    }

    /// <summary>The index in <see cref="VisibleEntries"/> of the first line on screen.</summary>
    public int TopIndex
    {
        get { lock (_gate) { return EffectiveTop(); } }
    }

    /// <summary>Adds text as one line per line break (a blank line stays a blank line). Safe to call from any thread.</summary>
    public void Append(string text, LogEntryLevel level = LogEntryLevel.Info)
    {
        text ??= string.Empty;
        bool parseAnsi;
        bool highlight;
        IReadOnlyList<LogHighlighter> highlighters;
        lock (_gate)
        {
            parseAnsi = _parseAnsi;
            highlight = _highlightingEnabled;
            highlighters = _highlighters;
        }

        // Colours carry from one line to the next, so the codes are read over the whole text before it is split.
        string plain = text;
        IReadOnlyList<LogSpan> ansiSpans = [];
        if (AnsiText.HasEscapes(text))
        {
            (plain, ansiSpans) = parseAnsi ? AnsiText.Parse(text) : (AnsiText.Parse(text).Plain, []);
        }

        var entries = new List<LogEntry>();
        int lineStart = 0;
        for (int i = 0; i <= plain.Length; i++)
        {
            bool atEnd = i == plain.Length;
            if (!atEnd && plain[i] is not ('\r' or '\n'))
            {
                continue;
            }

            string line = plain[lineStart..i].Replace('\t', ' ');
            int trimmed = line.TrimEnd().Length;
            line = line[..trimmed];
            var spans = new List<LogSpan>();
            foreach (LogSpan span in ansiSpans)
            {
                int start = Math.Max(span.Start, lineStart) - lineStart;
                int end = Math.Min(span.End, lineStart + trimmed) - lineStart;
                if (end > start)
                {
                    spans.Add(span with { Start = start, Length = end - start });
                }
            }

            if (highlight)
            {
                spans.AddRange(LogHighlighters.Find(line, highlighters, spans));
            }

            entries.Add(new LogEntry(line, level, spans.Count == 0 ? null : spans));
            if (!atEnd && plain[i] == '\r' && i + 1 < plain.Length && plain[i + 1] == '\n')
            {
                i++;
            }

            lineStart = i + 1;
        }

        lock (_gate)
        {
            foreach (LogEntry entry in entries)
            {
                _entries.Add(entry);
                if (Passes(entry))
                {
                    _visible.Add(entry);
                    if (!_follow)
                    {
                        _unread++;
                    }
                }
            }

            Trim();
            _version++;
        }
    }

    /// <summary>Removes every line.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _visible.Clear();
            _top = 0;
            _unread = 0;
            _version++;
        }
    }

    private bool Passes(LogEntry entry)
    {
        return entry.Level >= _minimumLevel
            && (_filterText.Length == 0 || entry.Text.Contains(_filterText, StringComparison.CurrentCultureIgnoreCase));
    }

    private void RebuildVisible()
    {
        _visible.Clear();
        _visible.AddRange(_entries.Where(Passes));
        _top = Math.Min(_top, Math.Max(0, _visible.Count - 1));
        _unread = 0;
        _version++;
    }

    /// <summary>Drops the oldest lines beyond <see cref="MaxLines"/>; a view that is not following keeps showing the same lines.</summary>
    private void Trim()
    {
        int excess = _entries.Count - _maxLines;
        if (excess <= 0)
        {
            return;
        }

        int hidden = _entries.Take(excess).Count(Passes);
        _entries.RemoveRange(0, excess);
        _visible.RemoveRange(0, Math.Min(hidden, _visible.Count));
        _top = Math.Max(0, _top - hidden);
    }

    private int Rows => Math.Max(1, (int)ViewSize.Height);

    private int MaxTop => Math.Max(0, _visible.Count - Rows);

    private int EffectiveTop() => _follow ? MaxTop : Math.Min(_top, MaxTop);

    private void ScrollTo(int top)
    {
        lock (_gate)
        {
            _top = Math.Clamp(top, 0, MaxTop);
            _follow = _top >= MaxTop;
            if (_follow)
            {
                _unread = 0;
            }

            _version++;
        }
    }

    private void ScrollBy(int rows)
    {
        int current;
        lock (_gate)
        {
            current = EffectiveTop();
        }

        ScrollTo(current + rows);
    }

    private void ScrollSideways(int columns)
    {
        lock (_gate)
        {
            _left = Math.Max(0, _left + columns);
            _version++;
        }
    }

    public override void OnIdle()
    {
        base.OnIdle();
        bool changed;
        lock (_gate)
        {
            changed = _version != _drawnVersion;
        }

        if (changed)
        {
            StateHasChanged();
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Up/Down", "Scroll one line");
        yield return ("PageUp/PageDown", "Scroll one page");
        yield return ("Home", "Oldest line");
        yield return ("End", "Newest line, and follow new lines");
        yield return ("Left/Right", "Scroll sideways");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled || keyEvent.Alt || keyEvent.Control)
        {
            return isHandled;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow:
                ScrollBy(-1);
                return true;
            case ConsoleKey.DownArrow:
                ScrollBy(1);
                return true;
            case ConsoleKey.PageUp:
                ScrollBy(-Rows);
                return true;
            case ConsoleKey.PageDown:
                ScrollBy(Rows);
                return true;
            case ConsoleKey.Home:
                ScrollTo(0);
                return true;
            case ConsoleKey.End:
                ScrollTo(int.MaxValue);
                return true;
            case ConsoleKey.LeftArrow:
                ScrollSideways(-ColumnStep);
                return true;
            case ConsoleKey.RightArrow:
                ScrollSideways(ColumnStep);
                return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        long x = (long)mouseEvent.Position.X - (long)AbsolutePosition.X;
        long y = (long)mouseEvent.Position.Y - (long)AbsolutePosition.Y;
        if (mouseEvent.WheelDelta != 0 && IsEnabled && x >= 0 && x < ViewSize.Width && y >= 0 && y < ViewSize.Height)
        {
            ScrollBy(mouseEvent.WheelDelta > 0 ? -WheelRows : WheelRows);
            return true;
        }

        return isHandled;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        LogEntry[] rows;
        int unread;
        int left;
        bool following;
        lock (_gate)
        {
            int top = EffectiveTop();
            rows = [.. _visible.Skip(top).Take(height)];
            unread = _unread;
            left = _left;
            following = _follow;
            _drawnVersion = _version;
        }

        for (int line = 0; line < height; line++)
        {
            string text = string.Empty;
            Color foreground = colorScheme.Foreground;
            if (line < rows.Length)
            {
                LogEntry entry = rows[line];
                text = left < entry.Text.Length ? entry.Text[left..] : string.Empty;
                foreground = ColourOf(entry.Level, colorScheme);
            }
            else if (line == 0)
            {
                text = "No log lines";
                foreground = colorScheme.DisabledForeground;
            }

            string badge = line == height - 1 && !following && unread > 0 ? $" ▼ {unread} new " : string.Empty;
            int textWidth = Math.Max(0, width - badge.Length);
            var conxelString = new ConxelString(text.Cut(textWidth).PadRight(textWidth), foreground, colorScheme.Background);
            if (line < rows.Length && rows[line].Spans is { } entrySpans)
            {
                ApplySpans(conxelString, entrySpans, left, Math.Min(text.Length, textWidth));
            }

            if (badge.Length > 0)
            {
                conxelString.Append(badge, colorScheme.SelectedForeground, colorScheme.SelectedBackground);
            }

            conxelString.Expand(width, foreground, colorScheme.Background);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + line), new Size(width, 1), conxelString);
        }
    }

    /// <summary>Colours the cells of a drawn row by the spans of its line; the row starts at text column <paramref name="left"/>.</summary>
    private void ApplySpans(ConxelString drawn, IReadOnlyList<LogSpan> spans, int left, int shown)
    {
        if (!IsEnabled)
        {
            return;
        }

        foreach (LogSpan span in spans)
        {
            for (int x = Math.Max(0, span.Start - left); x < Math.Min(shown, span.End - left); x++)
            {
                Conxel cell = drawn.String[x];
                if (span.Foreground is { } foreground)
                {
                    cell.ForegroundColor = foreground;
                }

                if (span.Background is { } background)
                {
                    cell.BackgroundColor = background;
                }

                cell.IsBold |= span.Bold;
                cell.IsItalic |= span.Italic;
                cell.IsUnderline |= span.Underline;
                drawn.String[x] = cell;
            }
        }
    }

    private Color ColourOf(LogEntryLevel level, ColorScheme colorScheme)
    {
        if (!IsEnabled)
        {
            return colorScheme.DisabledForeground;
        }

        return level switch
        {
            LogEntryLevel.Error => colorScheme.Error,
            LogEntryLevel.Warning => Color.Yellow,
            LogEntryLevel.Debug or LogEntryLevel.Trace => colorScheme.DisabledForeground,
            _ => colorScheme.Foreground,
        };
    }
}
