using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// An elapsed-time readout (<c>00:42</c>, <c>1:02:03</c> from an hour up) that runs between
/// <see cref="Start"/> and <see cref="Stop"/> and adds up across restarts until <see cref="Reset"/>. Space
/// toggles it and R resets it while it has the focus. The clock is the injected
/// <see cref="IDateTimeProviderService"/> and the control redraws only when the shown text changes, so an idle
/// pass costs a subtraction.
/// </summary>
public class ElapsedClock : Control
{
    private readonly IDateTimeProviderService _dateTimeProviderService;
    private TimeSpan _accumulated;
    private DateTime? _startedAt;
    private string _shown = string.Empty;

    public ElapsedClock(IThemeService themeService, IDateTimeProviderService dateTimeProviderService)
        : base(themeService)
    {
        _dateTimeProviderService = dateTimeProviderService;
        CanFocus = true;
        ViewSize = new Size(10, 1);
    }

    public string Label { get; set; } = string.Empty;

    public bool IsRunning => _startedAt is not null;

    public TimeSpan Elapsed => _startedAt is null
        ? _accumulated
        : _accumulated + (_dateTimeProviderService.Now - _startedAt.Value);

    public void Start()
    {
        if (_startedAt is null)
        {
            _startedAt = _dateTimeProviderService.Now;
            StateHasChanged();
        }
    }

    public void Stop()
    {
        if (_startedAt is not null)
        {
            _accumulated += _dateTimeProviderService.Now - _startedAt.Value;
            _startedAt = null;
            StateHasChanged();
        }
    }

    public void Reset()
    {
        _accumulated = TimeSpan.Zero;
        if (_startedAt is not null)
        {
            _startedAt = _dateTimeProviderService.Now;
        }

        StateHasChanged();
    }

    /// <summary>Formats <paramref name="elapsed"/>, rounding down to whole seconds.</summary>
    public static string Format(TimeSpan elapsed)
    {
        long seconds = (long)Math.Max(0, elapsed.TotalSeconds);
        long hours = seconds / 3600;
        long minutes = seconds % 3600 / 60;
        return hours > 0
            ? $"{hours}:{minutes:00}:{seconds % 60:00}"
            : $"{minutes:00}:{seconds % 60:00}";
    }

    public override void OnIdle()
    {
        if (_startedAt is not null)
        {
            string text = Format(Elapsed);
            if (text != _shown)
            {
                _shown = text;
                StateHasChanged();
            }
        }

        base.OnIdle();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled)
        {
            return isHandled;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Spacebar)
        {
            if (IsRunning)
            {
                Stop();
            }
            else
            {
                Start();
            }

            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.R && !keyEvent.Control && !keyEvent.Alt)
        {
            Reset();
            return true;
        }

        return false;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        string text = Format(Elapsed);
        _shown = text;
        int width = (int)ViewSize.Width;
        ConxelString line = new(Label.Length == 0 ? string.Empty : Label + " ", colorScheme.Foreground, colorScheme.Background);
        Color foreground = IsRunning ? Color.Green : colorScheme.Foreground;
        line.Append(text, IsFocused ? colorScheme.SelectedFocusedForeground : foreground, colorScheme.Background);
        line.Expand(width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
