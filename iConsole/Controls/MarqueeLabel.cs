using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// One line of text that scrolls when it is longer than the control, for a long title, a track name or a status that has
/// little room (see <see cref="Marquee"/>). Text that fits stays still. Time comes from <see cref="ITimeProvider"/>, and the
/// label repaints from <see cref="OnIdle"/> only when the text has moved a cell, so a label at rest costs nothing.
/// </summary>
public class MarqueeLabel : Control
{
    private readonly ITimeProvider _time;
    private string _text = string.Empty;
    private long _startedMilliseconds;
    private int _drawnOffset = -1;

    public MarqueeLabel(IThemeService themeService, ITimeProvider time) : base(themeService)
    {
        _time = time;
        _startedMilliseconds = time.MonotonicMilliseconds;
        CanFocus = false;
        ViewSize = new Size(20, 1);
    }

    public string Text
    {
        get => _text;
        set
        {
            string text = value ?? string.Empty;
            if (_text != text)
            {
                _text = text;
                // New text starts from its beginning, at rest.
                _startedMilliseconds = _time.MonotonicMilliseconds;
                StateHasChanged();
            }
        }
    }

    /// <summary>How long the text rests before it moves.</summary>
    public TimeSpan Pause { get; set; } = Marquee.DefaultPause;

    /// <summary>How long it takes to move one cell.</summary>
    public TimeSpan Step { get; set; } = Marquee.DefaultStep;

    /// <summary>Spaces between the end and the beginning coming round.</summary>
    public int Gap { get; set; } = Marquee.DefaultGap;

    private TimeSpan Elapsed => TimeSpan.FromMilliseconds(_time.MonotonicMilliseconds - _startedMilliseconds);

    /// <summary>How many cells the text has moved now.</summary>
    public int Offset => Marquee.Offset(_text.Length, (int)ViewSize.Width, Elapsed, Pause, Step, Gap);

    public override void OnIdle()
    {
        base.OnIdle();
        if (Offset != _drawnOffset)
        {
            StateHasChanged();
        }
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        int width = (int)ViewSize.Width;
        if (width <= 0 || ViewSize.Height <= 0)
        {
            return;
        }

        _drawnOffset = Offset;
        ColorScheme scheme = ThemeService.CurrentColorScheme;
        string shown = Marquee.Window(_text, width, Elapsed, Pause, Step, Gap);
        buffer.Write(ZOrder, AbsolutePosition, new Size(width, 1), new ConxelString(shown, scheme.Foreground, scheme.Background));
    }
}
