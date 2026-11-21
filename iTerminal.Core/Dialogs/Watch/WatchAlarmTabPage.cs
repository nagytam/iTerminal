using System.Globalization;
using iConsole.Controls;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model.Watch;
using iTerminal.Core.Services.Watch;

namespace iTerminal.Core.Dialogs.Watch;

/// <summary>
/// The alarms, in time order, with the next one that will ring above the list. An alarm rings every day at its time while it is
/// on; Space switches it on or off, N adds one (<c>07:30 Wake up</c>) and Delete removes it after a y. The alarms ring from the
/// background job, so they ring whether or not this dialog is open; they do not ring while iTerminal is closed.
/// </summary>
public sealed class WatchAlarmTabPage(WatchStore store, IDateTimeProviderService dateTimeProviderService, IThemeService themeService)
    : WatchListTabPage(UiText.Get("Watch.TabAlarm", "Alarm"), themeService)
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);

    protected override int ItemCount => Items().Count;

    protected override int HeadingRow => 0;

    protected override string HeadingText
    {
        get
        {
            var now = dateTimeProviderService.Now;
            var enabled = Items().Where(alarm => alarm.Enabled).ToList();
            if (enabled.Count == 0)
            {
                return " " + UiText.Get("Watch.NoAlarmSet", "No alarm is set");
            }

            var next = enabled
                .Select(alarm => (Alarm: alarm, Until: UntilNext(alarm, now)))
                .OrderBy(entry => entry.Until)
                .First();
            return string.Format(
                CultureInfo.CurrentCulture,
                " " + UiText.Get("Watch.NextAlarm", "Next alarm: {0}, in {1}"),
                next.Alarm.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
                Countdown.Format(next.Until));
        }
    }

    protected override string EmptyText => UiText.Get("Watch.NoAlarms", "No alarms. Press N to add one, e.g. 07:30 Wake up.");

    protected override string EntryPrompt => UiText.Get("Watch.NewAlarmPrompt", "New alarm (HH:mm label)");

    protected override string RowText(int index)
    {
        var alarm = Items()[index];
        string state = alarm.Enabled
            ? UiText.Get("Watch.On", "on")
            : UiText.Get("Watch.Off", "off");
        return $"{state.ExpandRight(4)}  {alarm.Time.ToString("HH:mm", CultureInfo.InvariantCulture)}  {alarm.DisplayLabel}";
    }

    protected override Color? RowForeground(int index) => Items()[index].Enabled ? null : DimColour;

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Up/Down", UiText.Get("DialogHint.MoveAlarm", "Choose an alarm"));
        yield return ("Space", UiText.Get("Watch.ToggleAlarm", "Switch it on or off"));
        yield return ("N/Insert", UiText.Get("Watch.AddAlarm", "Add an alarm"));
        yield return ("Delete", UiText.Get("Watch.RemoveAlarm", "Remove the alarm (asks first)"));
    }

    protected override string? AddItem(string text)
    {
        if (!WatchAlarm.TryParse(text, out var time, out var label, out var error))
        {
            return error;
        }

        store.AddAlarm(time, label);
        return null;
    }

    protected override void ToggleItem(int index)
    {
        var alarm = Items()[index];
        store.ReplaceAlarm(alarm with { Enabled = !alarm.Enabled });
    }

    protected override void DeleteItem(int index) => store.RemoveAlarm(Items()[index].Uid);

    /// <summary>The alarms in time order, then by label, so the list does not change its order when one is added.</summary>
    private List<WatchAlarm> Items() =>
        store.Alarms.OrderBy(alarm => alarm.Time).ThenBy(alarm => alarm.Label, StringComparer.Ordinal).ToList();

    /// <summary>
    /// How long until the alarm next rings, counted in whole minutes the way the alarm is set. An alarm that has already rung
    /// today waits for tomorrow, and one whose minute has come rings now.
    /// </summary>
    private static TimeSpan UntilNext(WatchAlarm alarm, DateTime now)
    {
        var minute = new TimeSpan(now.Hour, now.Minute, 0);
        var until = alarm.Time.ToTimeSpan() - minute;
        if (until < TimeSpan.Zero)
        {
            until += Day;
        }

        if (until == TimeSpan.Zero && alarm.LastRungOn == DateOnly.FromDateTime(now))
        {
            until = Day;
        }

        return until;
    }
}
