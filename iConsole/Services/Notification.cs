namespace iConsole.Services;

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Something the application wants the user to know without stopping them: a copy that finished
/// while they were looking elsewhere, a scan that failed. Immutable — how long it has left on screen
/// is the <see cref="INotificationService"/>'s business, not the notification's.
/// </summary>
/// <param name="Id">Unique for the run; what <see cref="INotificationService.Dismiss"/> takes.</param>
/// <param name="Title">The one line that has to be read.</param>
/// <param name="Message">Optional detail under the title.</param>
/// <param name="Duration">
/// How long it stays on screen once shown; <see cref="Timeout.InfiniteTimeSpan"/> keeps it until dismissed.
/// </param>
public sealed record Notification(
    long Id,
    string Title,
    string? Message,
    NotificationSeverity Severity,
    DateTime CreatedAt,
    TimeSpan Duration);
