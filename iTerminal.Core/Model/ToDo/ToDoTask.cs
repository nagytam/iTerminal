using System.Globalization;
using System.Text.Json;
using iTerminal.Core.Services;

namespace iTerminal.Core.Model.ToDo;

public enum ToDoPriority
{
    Normal,
    High,
    Low,
}

/// <summary>
/// One task: a subject, an optional due date, a priority, whether it is done and, optionally, the moment to be
/// reminded of it. Immutable, so a change is a <c>with</c> copy written back by <see cref="ToDoStore.Update"/>.
/// <see cref="RemindAt"/> is local time, and it is cleared once the reminder has been given (or the task is done).
/// </summary>
public sealed record ToDoTask(Guid Id, string Subject, DateOnly? DueDate, ToDoPriority Priority, bool IsComplete, DateTime? RemindAt = null)
{
    /// <summary>The form a reminder is typed in and read back in, so the edit line round-trips it.</summary>
    public const string ReminderFormat = "yyyy-MM-dd'T'HH:mm";

    /// <summary>Priority cycles Normal → High → Low → Normal, as the list's P key steps through it.</summary>
    public static ToDoPriority NextPriority(ToDoPriority priority) => priority switch
    {
        ToDoPriority.Normal => ToDoPriority.High,
        ToDoPriority.High => ToDoPriority.Low,
        _ => ToDoPriority.Normal,
    };

    /// <summary>
    /// Reads what the person typed: a leading <c>!</c> makes it high priority, a trailing <c>@yyyy-MM-dd</c> gives it a
    /// due date and a trailing <c>~time</c> sets a reminder (see <see cref="TryParseReminder"/>). The two trailing
    /// words may come in either order. <c>!Renew passport @2026-11-01 ~09:00</c> is a high-priority task due on
    /// 1 November 2026 that reminds at nine in the morning.
    /// </summary>
    public static bool TryParse(string text, Guid id, DateTime now, out ToDoTask? task, out string error)
    {
        task = null;
        var body = text.Trim();
        var priority = ToDoPriority.Normal;
        if (body.StartsWith('!'))
        {
            priority = ToDoPriority.High;
            body = body[1..].TrimStart();
        }

        var words = body.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        DateOnly? due = null;
        DateTime? remindAt = null;
        var sawReminder = false;
        while (words.Count > 0 && (words[^1].StartsWith('@') || words[^1].StartsWith('~')))
        {
            var word = words[^1];
            if (word.StartsWith('@'))
            {
                if (due is not null || !DateOnly.TryParseExact(word[1..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    error = due is not null
                        ? "A task has one due date"
                        : $"\"{word}\" is not a due date; use @yyyy-MM-dd, e.g. @2026-11-01";
                    return false;
                }

                due = date;
            }
            else
            {
                if (sawReminder)
                {
                    error = "A task has one reminder";
                    return false;
                }

                if (!TryParseReminder(word[1..], now, out var reminder, out error) || reminder is null)
                {
                    // A bare "~" parses as "clear", which is not a reminder: say so rather than report an empty message.
                    if (error.Length == 0)
                    {
                        error = $"\"{word}\" is not a reminder time; use ~HH:mm or ~yyyy-MM-ddTHH:mm";
                    }

                    return false;
                }

                remindAt = reminder;
                sawReminder = true;
            }

            words.RemoveAt(words.Count - 1);
        }

        var subject = string.Join(' ', words);
        if (subject.Length == 0)
        {
            error = "A task needs a subject";
            return false;
        }

        task = new ToDoTask(id, subject, due, priority, IsComplete: false, remindAt);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Reads a reminder time. <c>HH:mm</c> is the next time that clock reading comes round: today when it is still ahead,
    /// tomorrow when it has passed. <c>yyyy-MM-ddTHH:mm</c> is that exact moment and must still be ahead. Empty clears
    /// the reminder, and is accepted so that the reminder prompt can take one away. Times are local.
    /// </summary>
    public static bool TryParseReminder(string text, DateTime now, out DateTime? remindAt, out string error)
    {
        remindAt = null;
        var body = text.Trim();
        if (body.Length == 0)
        {
            error = string.Empty;
            return true;
        }

        if (TimeOnly.TryParseExact(body, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            var at = now.Date + time.ToTimeSpan();
            remindAt = at > now ? at : at.AddDays(1);
            error = string.Empty;
            return true;
        }

        if (DateTime.TryParseExact(body, ReminderFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            if (exact <= now)
            {
                error = "That reminder time has already passed";
                return false;
            }

            remindAt = exact;
            error = string.Empty;
            return true;
        }

        error = $"\"{body}\" is not a reminder time; use HH:mm or yyyy-MM-ddTHH:mm, e.g. 15:30 or 2026-11-01T09:00";
        return false;
    }
}

/// <summary>
/// The tasks the person keeps, as one JSON list in the settings table under <see cref="SettingsKey"/>. Kept
/// out of the database for the same reason as the calendar's meetings: a new table would change the
/// schema stamp and clear every cached table at start-up.
/// </summary>
/// <remarks>
/// The list dialog writes through this store on the UI thread and <see cref="TakeDueReminders"/> is called from the
/// reminder job's thread, so every member takes <c>_gate</c>.
/// </remarks>
public sealed class ToDoStore(ISettingsService settingsService)
{
    public const string SettingsKey = "ToDoTasks";

    private static readonly JsonSerializerOptions Json = new();

    private readonly object _gate = new();
    private readonly List<ToDoTask> _tasks = [];
    private bool _loaded;

    /// <summary>
    /// Open tasks first; then by due date with undated tasks last; then high priority ahead of normal ahead
    /// of low; then by subject. Done tasks stay in the list (Outlook keeps them) and sink below the open ones.
    /// </summary>
    public IReadOnlyList<ToDoTask> Visible(bool showCompleted)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _tasks
                .Where(t => showCompleted || !t.IsComplete)
                .OrderBy(t => t.IsComplete)
                .ThenBy(t => t.DueDate is null)
                .ThenBy(t => t.DueDate)
                .ThenBy(t => Rank(t.Priority))
                .ThenBy(t => t.Subject, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public void Add(ToDoTask task)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _tasks.Add(task);
            Save();
        }
    }

    /// <summary>Replaces the task with the same <see cref="ToDoTask.Id"/>.</summary>
    public void Update(ToDoTask task)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var index = _tasks.FindIndex(t => t.Id == task.Id);
            if (index < 0)
            {
                return;
            }

            _tasks[index] = task;
            Save();
        }
    }

    public void Remove(Guid id)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_tasks.RemoveAll(t => t.Id == id) > 0)
            {
                Save();
            }
        }
    }

    /// <summary>
    /// The open tasks whose reminder time has come by <paramref name="now"/>. Each is returned once: its reminder is
    /// cleared and saved in the same step, so a reminder is never given twice, even when the job and a restart race.
    /// </summary>
    public IReadOnlyList<ToDoTask> TakeDueReminders(DateTime now)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var due = new List<ToDoTask>();
            for (var i = 0; i < _tasks.Count; i++)
            {
                var task = _tasks[i];
                if (!task.IsComplete && task.RemindAt is { } at && at <= now)
                {
                    due.Add(task);
                    _tasks[i] = task with { RemindAt = null };
                }
            }

            if (due.Count > 0)
            {
                Save();
            }

            return due;
        }
    }

    private static int Rank(ToDoPriority priority) => priority switch
    {
        ToDoPriority.High => 0,
        ToDoPriority.Normal => 1,
        _ => 2,
    };

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var json = settingsService.Get(SettingsKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                // A null entry ("[null]") is not a task; the rest of the list is still good.
                _tasks.AddRange((JsonSerializer.Deserialize<List<ToDoTask>>(json, Json) ?? []).Where(task => task is not null));
            }
        }
        catch (JsonException)
        {
            // A damaged value must not stop the list opening; the next save replaces it.
        }
    }

    private void Save()
    {
        settingsService.Set(SettingsKey, JsonSerializer.Serialize(_tasks, Json));
        settingsService.Write();
    }
}
