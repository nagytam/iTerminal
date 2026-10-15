namespace iConsole.Services;

/// <summary>
/// Posts short, self-dismissing messages ("toasts") from anywhere in the application. Every member
/// is safe to call from any thread and returns at once: a notification is posted from a
/// <see cref="Tasks.TaskManager"/> worker as often as from a key handler.
///
/// <para>The service only keeps the lists. <see cref="Jobs.NotificationJob"/> runs the clock
/// (<see cref="Advance"/>) and <see cref="Controls.ToastHost"/> draws <see cref="Active"/>; neither
/// is needed to post, so code under test can notify with nothing else running.</para>
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Posts a notification. It is shown at once when there is room (<see cref="MaxVisible"/>) and
    /// waits its turn otherwise; its time on screen starts when it is shown. A notification equal in
    /// title, message and severity to one still on screen restarts that one's time instead of
    /// stacking a copy under it.
    /// </summary>
    /// <param name="duration">Null for the severity's default; <see cref="Timeout.InfiniteTimeSpan"/> to stay until dismissed.</param>
    Notification Notify(
        string title,
        string? message = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        TimeSpan? duration = null);

    /// <summary>The notifications on screen now, oldest first.</summary>
    IReadOnlyList<Notification> Active { get; }

    /// <summary>Everything posted this run, oldest first, capped at <see cref="MaxHistory"/>.</summary>
    IReadOnlyList<Notification> History { get; }

    /// <summary>How many notifications are shown at once; the rest wait.</summary>
    int MaxVisible { get; }

    /// <summary>How many notifications <see cref="History"/> keeps.</summary>
    int MaxHistory { get; }

    /// <summary>Moves whenever <see cref="Active"/> changes — what a control polls instead of subscribing.</summary>
    long Version { get; }

    /// <summary>Takes a notification off the screen (or out of the queue) before its time. False when it is not there.</summary>
    bool Dismiss(long id);

    void DismissAll();

    /// <summary>
    /// Takes <paramref name="elapsed"/> off the time every notification on screen has left, removes
    /// the ones that ran out and shows the ones that were waiting. Time is handed in rather than
    /// read from a clock so that whoever drives this decides when it passes — it does not while the
    /// terminal is lent to a shell, where nobody can read a toast.
    /// </summary>
    void Advance(TimeSpan elapsed);

    /// <summary>Raised after <see cref="Active"/> changed, on the thread that changed it.</summary>
    event EventHandler? Changed;
}
