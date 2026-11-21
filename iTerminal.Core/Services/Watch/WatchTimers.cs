using iTerminal.Core.Model.Watch;

namespace iTerminal.Core.Services.Watch;

/// <summary>What the Timers tab shows for one timer at one moment.</summary>
public readonly record struct WatchTimerRow(string Uid, string Label, TimeSpan Remaining, WatchTimerState State);

/// <summary>
/// The countdowns the person has started. They live for the session only and are not persisted. The list is shared by the
/// Timers tab (UI thread) and <see cref="Jobs.WatchAlarmJob"/> (its own thread), so every read and change takes the lock,
/// and the job's announcements go through <see cref="TakeFinished"/>.
/// </summary>
public sealed class WatchTimers
{
    private readonly Lock _gate = new();
    private readonly List<WatchTimer> _timers = [];

    public IReadOnlyList<WatchTimerRow> Rows(DateTime now)
    {
        lock (_gate)
        {
            return _timers.Select(timer => new WatchTimerRow(timer.Uid, timer.Label, timer.Remaining(now), timer.State(now))).ToList();
        }
    }

    public WatchTimer Add(string label, TimeSpan duration)
    {
        var timer = new WatchTimer(label, duration);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Toggle(string uid, DateTime now)
    {
        lock (_gate)
        {
            _timers.FirstOrDefault(timer => timer.Uid == uid)?.Toggle(now);
        }
    }

    public void Restart(string uid)
    {
        lock (_gate)
        {
            _timers.FirstOrDefault(timer => timer.Uid == uid)?.Restart();
        }
    }

    public void Remove(string uid)
    {
        lock (_gate)
        {
            _timers.RemoveAll(timer => timer.Uid == uid);
        }
    }

    /// <summary>The timers that have run out since the last call, each returned once. Marks them announced.</summary>
    public IReadOnlyList<WatchTimer> TakeFinished(DateTime now)
    {
        lock (_gate)
        {
            var finished = new List<WatchTimer>();
            foreach (var timer in _timers)
            {
                if (!timer.Announced && timer.State(now) == WatchTimerState.Done)
                {
                    timer.Announced = true;
                    finished.Add(timer);
                }
            }

            return finished;
        }
    }
}
