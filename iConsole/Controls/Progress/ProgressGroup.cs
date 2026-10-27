using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using System.Globalization;

namespace iConsole.Controls.Progress;

/// <summary>How a <see cref="ProgressTask"/> ended, or that it has not.</summary>
public enum ProgressState
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>The parts of a row a <see cref="ProgressGroup"/> can show.</summary>
[Flags]
public enum ProgressColumns
{
    None = 0,
    Description = 1,
    Bar = 2,
    Percent = 4,
    Speed = 8,
    Elapsed = 16,
    Remaining = 32,
    Default = Description | Bar | Percent | Remaining,
}

/// <summary>
/// One line of work in a <see cref="ProgressGroup"/>. Every member may be used from any thread. Time is read from the group's
/// <see cref="ITimeProvider"/>, so elapsed time, speed and the estimate of the time left move with a test's virtual clock.
/// </summary>
public sealed class ProgressTask
{
    private readonly ProgressGroup _group;
    private readonly Lock _gate = new();
    private string _description;
    private double _value;
    private double _maxValue;
    private bool _isIndeterminate;
    private ProgressState _state;
    private string _message = string.Empty;
    private readonly long _startedMilliseconds;
    private long _finishedMilliseconds;

    internal ProgressTask(ProgressGroup group, string description, double maxValue, bool isIndeterminate, long now)
    {
        _group = group;
        _description = description;
        _maxValue = Math.Max(0, maxValue);
        _isIndeterminate = isIndeterminate;
        _startedMilliseconds = now;
    }

    public string Description
    {
        get { lock (_gate) { return _description; } }
        set { lock (_gate) { _description = value ?? string.Empty; } _group.Changed(); }
    }

    /// <summary>The work done so far, 0 to <see cref="MaxValue"/>.</summary>
    public double Value
    {
        get { lock (_gate) { return _value; } }
        set { lock (_gate) { _value = Math.Clamp(double.IsFinite(value) ? value : 0, 0, _maxValue); } _group.Changed(); }
    }

    /// <summary>The work that makes the task complete; 0 or less leaves the bar empty.</summary>
    public double MaxValue
    {
        get { lock (_gate) { return _maxValue; } }
        set { lock (_gate) { _maxValue = Math.Max(0, double.IsFinite(value) ? value : 0); _value = Math.Min(_value, _maxValue); } _group.Changed(); }
    }

    /// <summary>Work whose total is not known: a sweeping block instead of a fill and a percentage.</summary>
    public bool IsIndeterminate
    {
        get { lock (_gate) { return _isIndeterminate; } }
        set { lock (_gate) { _isIndeterminate = value; } _group.Changed(); }
    }

    public ProgressState State
    {
        get { lock (_gate) { return _state; } }
    }

    /// <summary>Why the task failed, or a note about it; shown after the description when set.</summary>
    public string Message
    {
        get { lock (_gate) { return _message; } }
    }

    public bool IsFinished => State != ProgressState.Running;

    /// <summary>How much is done, 0 to 1.</summary>
    public double Fraction
    {
        get { lock (_gate) { return _maxValue <= 0 ? (_state == ProgressState.Succeeded ? 1 : 0) : _value / _maxValue; } }
    }

    /// <summary>Time since the task was added, or the time it ran for when finished.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            long end;
            lock (_gate)
            {
                end = _state == ProgressState.Running ? _group.Now : _finishedMilliseconds;
            }

            return TimeSpan.FromMilliseconds(Math.Max(0, end - _startedMilliseconds));
        }
    }

    /// <summary>Work per second since the task began; 0 before any time has passed.</summary>
    public double Speed
    {
        get
        {
            double seconds = Elapsed.TotalSeconds;
            return seconds <= 0 ? 0 : Value / seconds;
        }
    }

    /// <summary>The time left at the speed so far; null when it cannot be told (nothing done yet, no total, or finished).</summary>
    public TimeSpan? Remaining
    {
        get
        {
            double speed = Speed;
            lock (_gate)
            {
                if (_state != ProgressState.Running || _isIndeterminate || _maxValue <= 0 || speed <= 0)
                {
                    return null;
                }

                return TimeSpan.FromSeconds((_maxValue - _value) / speed);
            }
        }
    }

    /// <summary>Adds <paramref name="delta"/> to the work done.</summary>
    public void Increment(double delta)
    {
        lock (_gate)
        {
            _value = Math.Clamp(_value + (double.IsFinite(delta) ? delta : 0), 0, _maxValue);
        }

        _group.Changed();
    }

    /// <summary>Ends the task as done: the bar is full and the time stops.</summary>
    public void Complete()
    {
        Finish(ProgressState.Succeeded, string.Empty);
    }

    /// <summary>Ends the task as failed, keeping the work done so far.</summary>
    public void Fail(string message = "")
    {
        Finish(ProgressState.Failed, message);
    }

    private void Finish(ProgressState state, string message)
    {
        long now = _group.Now;
        lock (_gate)
        {
            if (_state != ProgressState.Running)
            {
                return;
            }

            _state = state;
            _message = message ?? string.Empty;
            _finishedMilliseconds = now;
            _isIndeterminate = false;
            if (state == ProgressState.Succeeded)
            {
                _value = _maxValue;
            }
        }

        _group.Changed();
    }
}

/// <summary>
/// Several tasks at once, one row each, in the manner of Spectre.Console's <c>Progress</c>: a marker (spinner, tick or cross),
/// the description, a bar, and the columns chosen in <see cref="Columns"/>. Tasks are added with <see cref="AddTask"/> and
/// updated from any thread; the group repaints from <see cref="OnIdle"/> (a changed task, a new second on the clock, or a moving
/// indeterminate bar), so workers never touch the control tree. With <see cref="ShowOverall"/> a last row sums the tasks.
/// </summary>
public class ProgressGroup : Control
{
    private const int MarkerWidth = 2;
    private const int PercentWidth = 5;
    private const int SpeedWidth = 12;
    private const int DurationWidth = 9;
    private const int MinBarWidth = 6;
    private const int SpinnerMilliseconds = 100;

    private static readonly char[] Spinner = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

    private readonly ITimeProvider _time;
    private readonly Lock _gate = new();
    private readonly List<ProgressTask> _tasks = [];
    private long _version;
    private long _drawnVersion = -1;
    private long _drawnBucket = -1;
    private bool _hideFinished;
    private bool _showOverall;
    private ProgressColumns _columns = ProgressColumns.Default;
    private string _unit = string.Empty;

    public ProgressGroup(IThemeService themeService, ITimeProvider time) : base(themeService)
    {
        _time = time;
        CanFocus = false;
        ViewSize = new Size(60, 5);
    }

    internal long Now => _time.MonotonicMilliseconds;

    internal void Changed() => Interlocked.Increment(ref _version);

    public ProgressColumns Columns
    {
        get => _columns;
        set { _columns = value; Changed(); }
    }

    /// <summary>The unit of the work, shown after the speed ("MB" gives "3.5 MB/s").</summary>
    public string Unit
    {
        get => _unit;
        set { _unit = value ?? string.Empty; Changed(); }
    }

    /// <summary>Drops a task's row once it has finished.</summary>
    public bool HideFinished
    {
        get => _hideFinished;
        set { _hideFinished = value; Changed(); }
    }

    /// <summary>A last row with the sum of all tasks.</summary>
    public bool ShowOverall
    {
        get => _showOverall;
        set { _showOverall = value; Changed(); }
    }

    /// <summary>The tasks, oldest first.</summary>
    public IReadOnlyList<ProgressTask> Tasks
    {
        get { lock (_gate) { return [.. _tasks]; } }
    }

    /// <summary>True when there is at least one task and every one has finished.</summary>
    public bool IsCompleted
    {
        get { lock (_gate) { return _tasks.Count > 0 && _tasks.All(t => t.IsFinished); } }
    }

    /// <summary>Adds a task and returns it for the worker to update. Safe from any thread.</summary>
    public ProgressTask AddTask(string description, double maxValue = 100, bool isIndeterminate = false)
    {
        var task = new ProgressTask(this, description ?? string.Empty, maxValue, isIndeterminate, Now);
        lock (_gate)
        {
            _tasks.Add(task);
        }

        Changed();
        return task;
    }

    public void Remove(ProgressTask task)
    {
        lock (_gate)
        {
            _tasks.Remove(task);
        }

        Changed();
    }

    /// <summary>Formats a duration as <c>m:ss</c> or <c>h:mm:ss</c>; <c>--:--</c> for none.</summary>
    public static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null || duration.Value < TimeSpan.Zero || duration.Value.TotalHours >= 100)
        {
            return "--:--";
        }

        TimeSpan d = duration.Value;
        return d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}"
            : $"{d.Minutes}:{d.Seconds:00}";
    }

    /// <summary>Formats a speed with the fewest digits that are still useful ("12/s", "3.5 MB/s", "0.25/s").</summary>
    public static string FormatSpeed(double perSecond, string unit)
    {
        string number = perSecond >= 100 ? perSecond.ToString("F0", CultureInfo.InvariantCulture)
            : perSecond >= 10 ? perSecond.ToString("F0", CultureInfo.InvariantCulture)
            : perSecond >= 1 ? perSecond.ToString("0.#", CultureInfo.InvariantCulture)
            : perSecond.ToString("0.##", CultureInfo.InvariantCulture);
        return (unit.Length > 0 ? number + " " + unit : number) + "/s";
    }

    public override void OnIdle()
    {
        base.OnIdle();
        long version = Interlocked.Read(ref _version);
        long bucket = TimeBucket();
        if (version != _drawnVersion || bucket != _drawnBucket)
        {
            StateHasChanged();
        }
    }

    /// <summary>Changes when something on screen moves with time: a second of elapsed time, or a step of a spinner or sweep.</summary>
    private long TimeBucket()
    {
        ProgressTask[] running = [.. Tasks.Where(t => !t.IsFinished)];
        if (running.Length == 0)
        {
            return 0;
        }

        return Now / SpinnerMilliseconds;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _drawnVersion = Interlocked.Read(ref _version);
        _drawnBucket = TimeBucket();
        ColorScheme scheme = ThemeService.CurrentColorScheme;
        var canvas = new CellGrid(width, height);
        ProgressTask[] all = [.. Tasks];
        ProgressTask[] tasks = [.. all.Where(t => !(_hideFinished && t.IsFinished))];
        int overallRows = _showOverall && all.Length > 0 ? 1 : 0;
        int taskRows = Math.Max(0, height - overallRows);
        // Too many tasks for the rows: keep the newest, since those are the ones still running.
        ProgressTask[] shown = tasks.Length > taskRows ? tasks[^taskRows..] : tasks;
        int descriptionWidth = DescriptionWidth(tasks, width);
        for (int row = 0; row < shown.Length; row++)
        {
            DrawRow(canvas, row, width, descriptionWidth, ViewOf(shown[row]), scheme);
        }

        if (overallRows == 1)
        {
            DrawRow(canvas, Math.Min(height - 1, shown.Length), width, descriptionWidth, Overall(all), scheme);
        }

        if (all.Length == 0)
        {
            canvas.Text(0, 0, "No tasks", scheme.DisabledForeground);
        }

        canvas.WriteTo(buffer, ZOrder, AbsolutePosition, scheme.Foreground, scheme.Background);
    }

    /// <summary>What a row shows; read once so one row is drawn from one moment.</summary>
    private sealed record RowView(
        string Description, ProgressState State, bool Indeterminate, double Fraction, double Speed, TimeSpan Elapsed, TimeSpan? Remaining, bool IsOverall);

    private RowView ViewOf(ProgressTask task)
    {
        string text = task.Description;
        if (task.Message.Length > 0)
        {
            text += ": " + task.Message;
        }

        return new RowView(text, task.State, task.IsIndeterminate, task.Fraction, task.Speed, task.Elapsed, task.Remaining, false);
    }

    private static RowView Overall(ProgressTask[] tasks)
    {
        double max = tasks.Sum(t => t.MaxValue);
        double done = tasks.Sum(t => t.Value);
        TimeSpan elapsed = tasks.Max(t => t.Elapsed);
        double speed = elapsed.TotalSeconds > 0 ? done / elapsed.TotalSeconds : 0;
        bool finished = tasks.All(t => t.IsFinished);
        ProgressState state = !finished ? ProgressState.Running : tasks.Any(t => t.State == ProgressState.Failed) ? ProgressState.Failed : ProgressState.Succeeded;
        TimeSpan? remaining = !finished && max > 0 && speed > 0 ? TimeSpan.FromSeconds((max - done) / speed) : null;
        return new RowView("Total", state, false, max <= 0 ? (finished ? 1 : 0) : done / max, speed, elapsed, remaining, true);
    }

    private int DescriptionWidth(ProgressTask[] tasks, int width)
    {
        if (!_columns.HasFlag(ProgressColumns.Description) || tasks.Length == 0)
        {
            return 0;
        }

        int longest = Math.Max(5, tasks.Max(t => t.Description.Length + (t.Message.Length > 0 ? t.Message.Length + 2 : 0)));
        return Math.Min(longest, Math.Max(8, width / 3));
    }

    private void DrawRow(CellGrid canvas, int row, int width, int descriptionWidth, RowView view, ColorScheme scheme)
    {
        Color stateColor = view.State switch
        {
            ProgressState.Succeeded => Color.Green,
            ProgressState.Failed => Color.Red,
            _ => Color.Cyan,
        };
        int x = 0;
        char marker = view.State switch
        {
            ProgressState.Succeeded => '✔',
            ProgressState.Failed => '✘',
            _ => Spinner[(int)((Now / SpinnerMilliseconds) % Spinner.Length)],
        };
        canvas.Set(x, row, marker, stateColor);
        x += MarkerWidth;

        // Columns on the right, in a fixed order; the bar takes what is left, and a column is dropped when the bar would be too small.
        var right = new List<(string Text, int Width, Color Color)>();
        if (_columns.HasFlag(ProgressColumns.Percent))
        {
            right.Add((view.Indeterminate ? string.Empty : (view.Fraction * 100).ToString("F0", CultureInfo.InvariantCulture) + "%", PercentWidth, scheme.Foreground));
        }

        if (_columns.HasFlag(ProgressColumns.Speed))
        {
            right.Add((view.Speed > 0 ? FormatSpeed(view.Speed, _unit) : string.Empty, SpeedWidth, scheme.DisabledForeground));
        }

        if (_columns.HasFlag(ProgressColumns.Elapsed))
        {
            right.Add((FormatDuration(view.Elapsed), DurationWidth, scheme.DisabledForeground));
        }

        if (_columns.HasFlag(ProgressColumns.Remaining))
        {
            right.Add((view.State == ProgressState.Running ? "ETA " + FormatDuration(view.Remaining) : string.Empty, DurationWidth + 4, scheme.DisabledForeground));
        }

        bool wantsBar = _columns.HasFlag(ProgressColumns.Bar);
        int rightWidth = right.Sum(r => r.Width + 1);
        int descWidth = descriptionWidth > 0 ? descriptionWidth + 1 : 0;
        while (right.Count > 0 && width - x - descWidth - rightWidth < (wantsBar ? MinBarWidth : 0))
        {
            rightWidth -= right[^1].Width + 1;
            right.RemoveAt(right.Count - 1);
        }

        if (descriptionWidth > 0)
        {
            string text = view.Description.Length > descriptionWidth ? view.Description[..Math.Max(0, descriptionWidth - 1)] + "…" : view.Description;
            canvas.Text(x, row, text, view.IsOverall ? scheme.SelectedForeground : scheme.Foreground);
            x += descWidth;
        }

        int barWidth = Math.Max(0, width - x - rightWidth);
        if (wantsBar && barWidth >= 3)
        {
            DrawBar(canvas, x, row, barWidth - 1, view, stateColor, scheme);
            x += barWidth;
        }
        else
        {
            x += barWidth;
        }

        foreach ((string text, int columnWidth, Color color) in right)
        {
            canvas.Text(x + 1, row, text.Length > columnWidth ? text[..columnWidth] : text.PadLeft(columnWidth), color);
            x += columnWidth + 1;
        }
    }

    private void DrawBar(CellGrid canvas, int x, int row, int barWidth, RowView view, Color stateColor, ColorScheme scheme)
    {
        if (view.Indeterminate)
        {
            int block = Math.Min(5, Math.Max(1, barWidth / 4));
            int travel = Math.Max(1, barWidth - block);
            int step = (int)((Now / SpinnerMilliseconds) % (travel * 2));
            int start = step <= travel ? step : (travel * 2) - step;
            for (int i = 0; i < barWidth; i++)
            {
                bool on = i >= start && i < start + block;
                canvas.Set(x + i, row, on ? '█' : '░', on ? stateColor : scheme.DisabledForeground);
            }

            return;
        }

        int filled = (int)Math.Round(Math.Clamp(view.Fraction, 0, 1) * barWidth);
        for (int i = 0; i < barWidth; i++)
        {
            canvas.Set(x + i, row, i < filled ? '█' : '░', i < filled ? stateColor : scheme.DisabledForeground);
        }
    }
}
