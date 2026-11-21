using System.Globalization;
using iConsole.Localization;

namespace iTerminal.Core.Model.Watch;

public enum WatchTimerState
{
    Ready,
    Running,
    Paused,
    Done,
}

/// <summary>
/// A countdown with a label. It starts, pauses and restarts; once its time is up it is <see cref="WatchTimerState.Done"/>
/// and stays so until it is restarted. <see cref="Announced"/> records whether the alarm job has already said so, so a
/// finished timer is announced once.
/// </summary>
public sealed class WatchTimer
{
    private readonly WatchRun _run = new();

    public WatchTimer(string label, TimeSpan duration)
    {
        Uid = Guid.NewGuid().ToString("N");
        Label = label;
        Duration = duration;
    }

    public string Uid { get; }

    public string Label { get; }

    public TimeSpan Duration { get; }

    public bool Announced { get; set; }

    public TimeSpan Remaining(DateTime now)
    {
        var left = Duration - _run.Elapsed(now);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    public WatchTimerState State(DateTime now)
    {
        if (Remaining(now) == TimeSpan.Zero)
        {
            return WatchTimerState.Done;
        }

        if (_run.IsRunning)
        {
            return WatchTimerState.Running;
        }

        return _run.Elapsed(now) > TimeSpan.Zero ? WatchTimerState.Paused : WatchTimerState.Ready;
    }

    /// <summary>Starts a stopped timer or pauses a running one. A finished timer does nothing: it has to be restarted.</summary>
    public void Toggle(DateTime now)
    {
        if (State(now) == WatchTimerState.Done)
        {
            return;
        }

        _run.Toggle(now);
    }

    /// <summary>Puts the timer back to its full duration, stopped. It can be announced again when it runs out.</summary>
    public void Restart()
    {
        _run.Reset();
        Announced = false;
    }

    /// <summary>
    /// Reads what the person typed: a duration, then an optional label. A bare number is minutes (<c>5 Tea</c>); otherwise
    /// <c>[h:]mm:ss</c> (<c>05:00 Eggs</c>, <c>1:30:00</c>). A zero duration is refused.
    /// </summary>
    public static bool TryParse(string text, out TimeSpan duration, out string label, out string error)
    {
        duration = TimeSpan.Zero;
        label = string.Empty;
        var tokens = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            error = UiText.Get("Watch.TypeDuration", "Type a duration and an optional label, e.g. 5 Tea or 05:00 Eggs");
            return false;
        }

        var token = tokens[0];
        bool parsed;
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes))
        {
            duration = TimeSpan.FromMinutes(minutes);
            parsed = minutes > 0;
        }
        else
        {
            parsed = TimeSpan.TryParseExact(token, ["h\\:mm\\:ss", "hh\\:mm\\:ss", "m\\:ss", "mm\\:ss"], CultureInfo.InvariantCulture, out duration)
                && duration > TimeSpan.Zero;
        }

        if (!parsed)
        {
            error = string.Format(CultureInfo.CurrentCulture, UiText.Get("Watch.BadDuration", "\"{0}\" is not a duration; type minutes, or m:ss, e.g. 5 or 05:00"), token);
            return false;
        }

        label = string.Join(' ', tokens[1..]);
        error = string.Empty;
        return true;
    }
}
