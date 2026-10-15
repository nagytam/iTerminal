using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iConsole.Tasks;

namespace iConsole.Controls.Progress;

public class ProgressTime : Control
{
    public string LabelElapsed { get; set; } = "Elapsed time";
    public string LabelRemaining { get; set; } = "Remaining time";
    public string LabelTotal { get; set; } = "Total time";
    public BackgroundTask Task { get; set; } 

    public ProgressTime(BackgroundTask task, IThemeService themeService) : base(themeService)
    {
        Task = task;
        ViewSize = new Data.Size(0, 1);
    }

    public override void OnIdle()
    {
        if (Task.Status == Data.TaskStatus.Started)
        {
            StateHasChanged();
        }
        base.OnIdle();
    }

    public override void Draw(IBuffer buffer)
    {
        var width = ViewSize.Width - 2;
        var line = string.Empty;
        if (Task.Status == Data.TaskStatus.Started)
        {
            line += $"{LabelElapsed}: {Task.Stopwatch.Elapsed:hh\\:mm\\:ss}";
        }
        else
        {
            line += $"{LabelTotal}: {Task.Stopwatch.Elapsed:hh\\:mm\\:ss}";
        }
        if (Task.Status == Data.TaskStatus.Started)
        {
            line += $" {LabelRemaining}: {Task.RemainingTime:hh\\:mm\\:ss}";
        }
        line = line.ExpandRight((int)width);
        var visualState = GetVisualState();
        GetColor(ThemeService.CurrentColorScheme, visualState, out var foreground, out var background);
        var lineString = new ConxelString(line, foreground, background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, lineString);
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        switch (visualState)
        {
            case VisualState.Disabled:
                foregroundColor = colorScheme.DisabledForeground;
                backgroundColor = colorScheme.DisabledBackground;
                break;
            case VisualState.Focused:
            case VisualState.Selected:
                foregroundColor = colorScheme.SelectedFocusedForeground;
                backgroundColor = colorScheme.SelectedBackground;
                break;
            default:
                foregroundColor = colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
                break;
        }
    }
}
