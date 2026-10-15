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
/// One task: a subject, an optional due date, a priority and whether it is done. Immutable, so a change
/// is a <c>with</c> copy written back by <see cref="ToDoStore.Update"/>.
/// </summary>
public sealed record ToDoTask(Guid Id, string Subject, DateOnly? DueDate, ToDoPriority Priority, bool IsComplete)
{
    /// <summary>Priority cycles Normal → High → Low → Normal, as the list's P key steps through it.</summary>
    public static ToDoPriority NextPriority(ToDoPriority priority) => priority switch
    {
        ToDoPriority.Normal => ToDoPriority.High,
        ToDoPriority.High => ToDoPriority.Low,
        _ => ToDoPriority.Normal,
    };

    /// <summary>
    /// Reads what the person typed: a leading <c>!</c> makes it high priority, a trailing
    /// <c>@yyyy-MM-dd</c> gives it a due date, and the rest is the subject. <c>!Renew passport @2026-11-01</c>
    /// is a high-priority task due on 1 November 2026.
    /// </summary>
    public static bool TryParse(string text, Guid id, out ToDoTask? task, out string error)
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
        if (words.Count > 0 && words[^1].StartsWith('@'))
        {
            if (!DateOnly.TryParseExact(words[^1][1..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                error = $"\"{words[^1]}\" is not a due date; use @yyyy-MM-dd, e.g. @2026-11-01";
                return false;
            }

            due = date;
            words.RemoveAt(words.Count - 1);
        }

        var subject = string.Join(' ', words);
        if (subject.Length == 0)
        {
            error = "A task needs a subject";
            return false;
        }

        task = new ToDoTask(id, subject, due, priority, IsComplete: false);
        error = string.Empty;
        return true;
    }
}

/// <summary>
/// The tasks the person keeps, as one JSON list in the settings table under <see cref="SettingsKey"/>. Kept
/// out of the database for the same reason as the calendar's meetings: a new table would change the
/// schema stamp and clear every cached table at start-up.
/// </summary>
public sealed class ToDoStore(ISettingsService settingsService)
{
    public const string SettingsKey = "ToDoTasks";

    private static readonly JsonSerializerOptions Json = new();

    private readonly List<ToDoTask> _tasks = [];
    private bool _loaded;

    /// <summary>
    /// Open tasks first; then by due date with undated tasks last; then high priority ahead of normal ahead
    /// of low; then by subject. Done tasks stay in the list (Outlook keeps them) and sink below the open ones.
    /// </summary>
    public IReadOnlyList<ToDoTask> Visible(bool showCompleted)
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

    public void Add(ToDoTask task)
    {
        EnsureLoaded();
        _tasks.Add(task);
        Save();
    }

    /// <summary>Replaces the task with the same <see cref="ToDoTask.Id"/>.</summary>
    public void Update(ToDoTask task)
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

    public void Remove(Guid id)
    {
        EnsureLoaded();
        if (_tasks.RemoveAll(t => t.Id == id) > 0)
        {
            Save();
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
                _tasks.AddRange(JsonSerializer.Deserialize<List<ToDoTask>>(json, Json) ?? []);
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
