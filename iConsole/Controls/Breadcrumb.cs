using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A one-line trail (<c>Home › Projects › iTerminal</c>) whose segments can be clicked. When the trail is
/// wider than the control the middle is folded into <c>…</c> so the first and the last segments — where
/// the user is — stay readable. Left/Right move the highlighted segment, Enter or a click raises
/// <see cref="SegmentActivated"/> with its index.
/// </summary>
public class Breadcrumb : Control
{
    private const string Separator = " › ";

    private List<string> _segments = [];
    private int _activeIndex;

    public Breadcrumb(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(40, 1);
    }

    public event EventHandler<int>? SegmentActivated;

    public IReadOnlyList<string> Segments
    {
        get => _segments;
        set
        {
            _segments = [.. value ?? []];
            _activeIndex = Math.Max(0, _segments.Count - 1);
            StateHasChanged();
        }
    }

    /// <summary>The highlighted segment; the last one (the current location) until the user moves.</summary>
    public int ActiveIndex
    {
        get => _activeIndex;
        set
        {
            _activeIndex = Math.Clamp(value, 0, Math.Max(0, _segments.Count - 1));
            StateHasChanged();
        }
    }

    /// <summary>
    /// The segments that are drawn for <paramref name="width"/> cells, as (index, text) pairs; index -1 is
    /// the folded-away middle. Segments are dropped from just after the first until the trail fits.
    /// </summary>
    public static List<(int Index, string Text)> Layout(IReadOnlyList<string> segments, int width, int keep = -1)
    {
        List<(int Index, string Text)> all = [.. segments.Select((text, index) => (index, text))];
        if (all.Count == 0 || Measure(all) <= width || all.Count < 3)
        {
            return all;
        }

        for (int dropped = 1; dropped < all.Count - 1; dropped++)
        {
            // The highlighted segment must stay on screen, or the arrow keys move a highlight nobody can see.
            if (keep >= 1 && keep <= dropped)
            {
                break;
            }

            List<(int Index, string Text)> folded = [all[0], (-1, "…"), .. all.Skip(1 + dropped)];
            if (Measure(folded) <= width)
            {
                return folded;
            }
        }

        if (keep == 0)
        {
            // Nothing fits: the first segment is the highlighted one, so it is the one that stays.
            return [all[0], (-1, "…")];
        }

        return keep >= 1 && keep < all.Count - 1 ? [all[0], (-1, "…"), all[keep]] : [(-1, "…"), all[^1]];
    }

    private static int Measure(List<(int Index, string Text)> parts)
        => parts.Sum(p => p.Text.Length) + Separator.Length * Math.Max(0, parts.Count - 1);

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled || _segments.Count == 0)
        {
            yield break;
        }

        yield return ("Left/Right", "Move the highlight along the trail");
        yield return ("Home/End", "First or last segment");
        yield return ("Enter/Space", "Open the highlighted segment");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled || _segments.Count == 0)
        {
            return isHandled;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.LeftArrow:
                ActiveIndex--;
                return true;
            case ConsoleKey.RightArrow:
                ActiveIndex++;
                return true;
            case ConsoleKey.Home:
                ActiveIndex = 0;
                return true;
            case ConsoleKey.End:
                ActiveIndex = _segments.Count - 1;
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                SegmentActivated?.Invoke(this, _activeIndex);
                return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (IsEnabled && mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            ActiveIndex += mouseEvent.WheelDelta > 0 ? -1 : 1;
            return true;
        }

        if (!IsEnabled || !mouseEvent.LeftClicked || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        int column = (int)mouseEvent.Position.X - (int)AbsolutePosition.X;
        int start = 0;
        if (!IsFocused)
        {
            SetFocus();
        }

        foreach ((int index, string text) in Layout(_segments, (int)ViewSize.Width, _activeIndex))
        {
            if (index >= 0 && column >= start && column < start + text.Length)
            {
                ActiveIndex = index;
                SegmentActivated?.Invoke(this, index);
                return true;
            }

            start += text.Length + Separator.Length;
        }

        return true;
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
        ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
        List<(int Index, string Text)> parts = Layout(_segments, width, _activeIndex);
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                line.Append(Separator, colorScheme.DisabledForeground, colorScheme.Background);
            }

            bool active = parts[i].Index == _activeIndex && parts[i].Index >= 0;
            if (!IsEnabled)
            {
                line.Append(parts[i].Text, colorScheme.DisabledForeground, colorScheme.Background);
            }
            else if (active && IsFocused)
            {
                line.Append(parts[i].Text, colorScheme.SelectedFocusedForeground, colorScheme.SelectedFocusedBackground);
            }
            else
            {
                line.Append(parts[i].Text, parts[i].Index < 0 ? colorScheme.DisabledForeground : colorScheme.Foreground, colorScheme.Background);
            }
        }

        line.Expand(width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
