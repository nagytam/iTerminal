using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model.Watch;
using iTerminal.Core.Services.Watch;

namespace iTerminal.Core.Dialogs.Watch;

/// <summary>
/// The countdowns started in this session. N starts one at once (<c>5 Tea</c> for five minutes, <c>05:00 Eggs</c> for the same
/// in m:ss), Space pauses or resumes the one under the cursor, R puts it back to its full time, and Delete removes it after a y.
/// When one runs out the job announces it, whether or not this dialog is open. The timers are not kept after iTerminal closes.
/// </summary>
public sealed class WatchTimersTabPage(WatchTimers timers, IDateTimeProviderService dateTimeProviderService, IThemeService themeService)
    : WatchListTabPage(UiText.Get("Watch.TabTimers", "Timers"), themeService)
{
    private const int StateWidth = 8;

    protected override int ItemCount => Rows().Count;

    protected override string EmptyText => UiText.Get("Watch.NoTimers", "No timers. Press N to start one, e.g. 5 Tea or 05:00 Eggs.");

    protected override string EntryPrompt => UiText.Get("Watch.NewTimerPrompt", "New timer (minutes, or m:ss, then a label)");

    protected override string RowText(int index)
    {
        var row = Rows()[index];
        string label = row.Label.Length == 0 ? UiText.Get("Watch.DefaultTimerLabel", "Timer") : row.Label;
        return $"{StateText(row.State).ExpandRight(StateWidth)} {Countdown.Format(row.Remaining),8}  {label}";
    }

    protected override Color? RowForeground(int index) => Rows()[index].State == WatchTimerState.Done ? Color.Red : null;

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Up/Down", UiText.Get("DialogHint.MoveTimer", "Choose a timer"));
        yield return ("Space", UiText.Get("Watch.StartPauseTimer", "Start or pause"));
        yield return ("R", UiText.Get("Watch.RestartTimer", "Restart it"));
        yield return ("N/Insert", UiText.Get("Watch.AddTimer", "Start a timer"));
        yield return ("Delete", UiText.Get("Watch.RemoveTimer", "Remove the timer (asks first)"));
    }

    protected override string? AddItem(string text)
    {
        if (!WatchTimer.TryParse(text, out var duration, out var label, out var error))
        {
            return error;
        }

        // A new timer starts at once: typing a duration is asking for it to run.
        var timer = timers.Add(label, duration);
        timers.Toggle(timer.Uid, dateTimeProviderService.Now);
        return null;
    }

    protected override void ToggleItem(int index) => timers.Toggle(Rows()[index].Uid, dateTimeProviderService.Now);

    protected override void DeleteItem(int index) => timers.Remove(Rows()[index].Uid);

    protected override Task<bool> HandleOtherKeyAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        if (keyEvent.ConsoleKey != ConsoleKey.R || !plain)
        {
            return Task.FromResult(false);
        }

        int index = Math.Clamp(Cursor, 0, Math.Max(0, ItemCount - 1));
        if (ItemCount > 0)
        {
            timers.Restart(Rows()[index].Uid);
        }

        return Task.FromResult(true);
    }

    private IReadOnlyList<WatchTimerRow> Rows() => timers.Rows(dateTimeProviderService.Now);

    private static string StateText(WatchTimerState state) => state switch
    {
        WatchTimerState.Running => UiText.Get("Watch.StateRunning", "Running"),
        WatchTimerState.Paused => UiText.Get("Watch.StatePaused", "Paused"),
        WatchTimerState.Done => UiText.Get("Watch.StateDone", "Done"),
        _ => UiText.Get("Watch.StateReady", "Ready"),
    };
}
