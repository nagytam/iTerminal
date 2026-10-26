using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// Time left until <see cref="Target"/> as <c>MM:SS</c> (or <c>H:MM:SS</c> from an hour up), counting down
/// once a second and raising <see cref="Finished"/> once when it reaches zero — for a confirmation that
/// expires, a retry after a back-off, a session timeout. The clock is the injected
/// <see cref="IDateTimeProviderService"/>, so the control is testable without waiting. The control is
/// redrawn only when the displayed text changes.
/// </summary>
public class Countdown : Control
{
    private readonly IDateTimeProviderService _dateTimeProviderService;
    private DateTime? _target;
    private string _shown = string.Empty;
    private bool _finishedRaised;

    public Countdown(IThemeService themeService, IDateTimeProviderService dateTimeProviderService)
        : base(themeService)
    {
        _dateTimeProviderService = dateTimeProviderService;
        ViewSize = new Size(8, 1);
    }

    private string _label = string.Empty;

    public string Label
    {
        get => _label;
        set
        {
            _label = value ?? string.Empty;
            StateHasChanged();
        }
    }

    public event EventHandler? Finished;

    /// <summary>When the countdown ends; null means idle (draws <c>--:--</c>).</summary>
    public DateTime? Target
    {
        get => _target;
        set
        {
            _target = value;
            _finishedRaised = false;
            UpdateShown();
        }
    }

    /// <summary>Counts down <paramref name="duration"/> from now; one that runs past the last date counts down to that date instead of throwing.</summary>
    public void Start(TimeSpan duration)
    {
        DateTime now = _dateTimeProviderService.Now;
        Target = duration >= DateTime.MaxValue - now ? DateTime.MaxValue
            : duration <= DateTime.MinValue - now ? DateTime.MinValue
            : now + duration;
    }

    public void Stop() => Target = null;

    public TimeSpan Remaining => _target is null
        ? TimeSpan.Zero
        : TimeSpan.FromTicks(Math.Max(0, (_target.Value - _dateTimeProviderService.Now).Ticks));

    public bool IsFinished => _target is not null && Remaining == TimeSpan.Zero;

    /// <summary>Formats <paramref name="remaining"/>, rounding up so the display reads 00:01 until the very end.</summary>
    public static string Format(TimeSpan remaining)
    {
        long seconds = (long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        long hours = seconds / 3600;
        long minutes = seconds % 3600 / 60;
        return hours > 0
            ? $"{hours}:{minutes:00}:{seconds % 60:00}"
            : $"{minutes:00}:{seconds % 60:00}";
    }

    private string Text => _target is null ? "--:--" : Format(Remaining);

    private void UpdateShown()
    {
        _shown = Text;
        StateHasChanged();
    }

    public override void OnIdle()
    {
        if (_target is not null)
        {
            string text = Text;
            if (text != _shown)
            {
                _shown = text;
                StateHasChanged();
            }

            if (!_finishedRaised && IsFinished)
            {
                _finishedRaised = true;
                Finished?.Invoke(this, EventArgs.Empty);
            }
        }

        base.OnIdle();
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
        string text = Text;
        // The time is what the control is for, so the label gives way to it: it is cut to the cells left over.
        int labelRoom = Math.Max(0, width - text.Length - 1);
        string prefix = Label.Length == 0 || labelRoom == 0 ? string.Empty : Label.Cut(labelRoom) + " ";
        Color foreground = _target is null || !IsEnabled ? colorScheme.DisabledForeground
            : IsFinished ? Color.Red
            : Remaining < TimeSpan.FromSeconds(10) ? Color.Yellow
            : colorScheme.Foreground;
        ConxelString line = new(prefix, colorScheme.Foreground, colorScheme.Background);
        line.Append(text, foreground, colorScheme.Background);
        line.Expand(width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
