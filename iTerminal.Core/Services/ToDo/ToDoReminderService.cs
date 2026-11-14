using iConsole.Services;
using iTerminal.Core.Model.ToDo;

namespace iTerminal.Core.Services.ToDo;

/// <summary>
/// Posts a notification for every open task whose reminder time has come. The notification stays on screen until it
/// is dismissed, so a reminder given while the person is away from the keyboard is still there when they return.
/// </summary>
public sealed class ToDoReminderService(ToDoStore store, INotificationService notificationService)
{
    /// <summary>Gives the reminders due by <paramref name="now"/>, and returns how many were given.</summary>
    public int GiveDueReminders(DateTime now)
    {
        var due = store.TakeDueReminders(now);
        var title = iConsole.Localization.UiText.Get("ToDo.ReminderTitle", "Reminder");
        foreach (var task in due)
        {
            notificationService.Notify(title, task.Subject, NotificationSeverity.Warning, Timeout.InfiniteTimeSpan);
        }

        return due.Count;
    }
}
