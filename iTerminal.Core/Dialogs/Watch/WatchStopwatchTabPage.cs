using System.Globalization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model.Watch;

namespace iTerminal.Core.Dialogs.Watch;

/// <summary>
/// A stopwatch in large digits, with its laps under it, newest first. Space starts or stops it, L takes a lap while it runs,
/// and R clears the time and the laps once it is stopped. It is kept for the session, so closing the dialog does not lose it.
/// </summary>
public sealed class WatchStopwatchTabPage(WatchStopwatch stopwatch, IDateTimeProviderService dateTimeProviderService, IThemeService themeService)
    : WatchTabPage(UiText.Get("Watch.TabStopwatch", "Stopwatch"), themeService)
{
    private const int DigitsRow = 2;
    private const int LapsHeadingRow = 8;
    private const int LapsTop = 9;

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Space", UiText.Get("Watch.StartStopWatch", "Start or stop"));
        yield return ("L", UiText.Get("Watch.Lap", "Take a lap"));
        yield return ("R", UiText.Get("Watch.ResetWatch", "Reset (when stopped)"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        var now = dateTimeProviderService.Now;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Spacebar when plain:
                stopwatch.Toggle(now);
                break;
            case ConsoleKey.L when plain:
                stopwatch.Lap(now);
                break;
            case ConsoleKey.R when plain:
                if (!stopwatch.IsRunning)
                {
                    stopwatch.Reset();
                }

                break;
            default:
                return await base.HandleKeyEventAsync(keyEvent);
        }

        StateHasChanged();
        return true;
    }

    protected override void PaintPage(IBuffer buffer)
    {
        var now = dateTimeProviderService.Now;
        var elapsed = stopwatch.Elapsed(now);
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        var (foreground, background) = RowColours(highlighted: false);

        string status = UiText.Get("Watch.StateReady", "Ready");
        if (stopwatch.IsRunning) status = UiText.Get("Watch.StateRunning", "Running");
        else if (elapsed > TimeSpan.Zero) status = UiText.Get("Watch.StateStopped", "Stopped");
        for (int row = 0; row < height; row++)
        {
            WriteRow(buffer, row, string.Empty, foreground, background);
        }

        WriteRow(buffer, 0, " " + status, DimColour, background);

        string time = WatchStopwatch.Format(elapsed);
        var digits = Digits.Render(time, DigitsStyle.Large);
        int offset = Math.Max(0, (width - Digits.Measure(time, DigitsStyle.Large)) / 2);
        for (int line = 0; line < digits.Count; line++)
        {
            WriteRow(buffer, DigitsRow + line, new string(' ', offset) + digits[line], foreground, background);
        }

        var laps = stopwatch.Laps;
        if (laps.Count == 0)
        {
            if (LapsTop < height - 1)
            {
                WriteRow(buffer, LapsTop, " " + UiText.Get("Watch.NoLaps", "No laps yet. Press L while it runs."), DimColour, background);
            }

            return;
        }

        WriteRow(buffer, LapsHeadingRow, " " + UiText.Get("Watch.Laps", "Laps"), foreground, background);
        int visible = Math.Max(0, height - 1 - LapsTop);
        for (int line = 0; line < visible && line < laps.Count; line++)
        {
            int index = laps.Count - 1 - line;
            var split = index == 0 ? laps[index] : laps[index] - laps[index - 1];
            string text = string.Format(
                CultureInfo.InvariantCulture,
                " {0,3}  {1}   {2}",
                index + 1,
                WatchStopwatch.Format(laps[index]),
                WatchStopwatch.Format(split));
            WriteRow(buffer, LapsTop + line, text, foreground, background);
        }
    }
}
