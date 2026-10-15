using iConsole.Buffers;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

public class Spinner : Control
{
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly IDateTimeProviderService _dateTimeProviderService;
    private int _frameIndex = 0;
    private DateTime _lastFrameChange;

    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    /// <summary>How long each frame is shown. OnIdle is driven by the render loop (up to ~1000/s), so
    /// frame advancement is gated by elapsed wall-clock time rather than idle-tick count — otherwise the
    /// spin rate would track render-loop frequency instead of a fixed, human-perceivable cadence.</summary>
    public TimeSpan FrameInterval { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Overrides the theme's foreground/background for callers embedding the spinner in a differently-colored line. Null falls back to the current color scheme.</summary>
    public Color? Foreground { get; set; }
    public Color? Background { get; set; }

    public Spinner(IThemeService themeService, IDateTimeProviderService dateTimeProviderService)
        : base(themeService)
    {
        _dateTimeProviderService = dateTimeProviderService;
        _lastFrameChange = _dateTimeProviderService.Now;
    }

    public override void OnIdle()
    {
        if (IsActive)
        {
            var now = _dateTimeProviderService.Now;
            if (now - _lastFrameChange >= FrameInterval)
            {
                _frameIndex = (_frameIndex + 1) % Frames.Length;
                _lastFrameChange = now;
                StateHasChanged();
            }
        }
        base.OnIdle();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        var frame = IsActive ? Frames[_frameIndex] : " ";
        var text = string.IsNullOrEmpty(Label) ? frame : frame + " " + Label;
        text = text.PadRight((int)ViewSize.Width);
        var s = new ConxelString(text, Foreground ?? colorScheme.Foreground, Background ?? colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
