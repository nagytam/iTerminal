using iConsole.Data;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using System.ComponentModel;

namespace iTerminal.Core.Helper;

/// <summary>
/// Posts a notification when a copy, move, delete, compress or extract is over.
///
/// The progress window is the only other place the outcome is shown, and it is gone the moment the
/// task succeeds — or earlier, when the user closed it to get on with something else, which is the
/// case a notification is for: the operation finishes behind whatever they went on to do.
/// </summary>
public static class TaskNotificationHelper
{
    /// <summary>
    /// Notifies once, when <paramref name="task"/> finishes — or at once when it already has: a small
    /// local copy is regularly over before the caller gets as far as this, and the one status change
    /// a task raises would then have gone by unheard.
    /// </summary>
    public static void NotifyWhenFinished(BackgroundTask task, INotificationService? notificationService)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (notificationService is null)
        {
            return;
        }

        var notified = 0;

        void NotifyIfFinished()
        {
            // The status can change between the subscription and the check below, on a worker
            // thread: both paths come through here and only the first one posts.
            if (task.Status == iConsole.Data.TaskStatus.Started
                || Interlocked.Exchange(ref notified, 1) != 0)
            {
                return;
            }

            task.PropertyChanged -= OnTaskChanged;
            var (title, message, severity) = Describe(task);
            notificationService.Notify(title, message, severity);
        }

        void OnTaskChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BackgroundTask.Status))
            {
                NotifyIfFinished();
            }
        }

        task.PropertyChanged += OnTaskChanged;
        NotifyIfFinished();
    }

    /// <summary>
    /// Turns a finished task into a title and a message. The tasks describe themselves for the
    /// progress window, in one of two shapes: a heading, what it was done to, and a last line saying
    /// how it ended ("Copy file / &lt;from&gt; / to / &lt;to&gt; / finished"), or a sentence that
    /// already says everything ("Compressed 3 file(s) into a.zip").
    /// </summary>
    internal static (string Title, string? Message, NotificationSeverity Severity) Describe(BackgroundTask task)
    {
        var lines = (task.Description ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        var heading = lines.Count > 0 ? lines[0] : task.Name;
        var endsWithOutcome = lines.Count > 1 && IsOutcome(lines[^1]);
        var details = endsWithOutcome ? lines[1..^1] : lines.Count > 1 ? lines[1..] : [];
        var detail = !string.IsNullOrWhiteSpace(task.Subject) ? task.Subject
            : details.Count > 0 ? string.Join(" ", details)
            : null;

        if (task.Error is not null)
        {
            // "Compress failed: <the message>" — the message is the Error, shown once, underneath.
            var failedAt = heading.IndexOf(" failed:", StringComparison.OrdinalIgnoreCase);
            var title = failedAt > 0 ? heading[..(failedAt + " failed".Length)]
                : endsWithOutcome ? $"{heading} failed"
                : heading;
            return (title, task.Error, NotificationSeverity.Error);
        }

        if (task.Status == iConsole.Data.TaskStatus.Canceled)
        {
            return (endsWithOutcome ? $"{heading} cancelled" : heading, detail, NotificationSeverity.Warning);
        }

        return (endsWithOutcome ? $"{heading} finished" : heading, detail, NotificationSeverity.Success);
    }

    /// <summary>
    /// A transfer in a few words: the entry's name and the folder it goes to ("readme.txt to target"),
    /// with the new name too when the transfer renames it ("a.txt to target/b.txt"). Names rather than
    /// paths: two of those do not fit a toast, and the panels already show where the folders are.
    /// </summary>
    public static string TransferSubject(Url sourceUrl, Url targetUrl)
    {
        var name = FocusedEntry.NameOf(sourceUrl);
        var targetPath = (targetUrl?.FullUrl ?? string.Empty).TrimEnd('/', '\\');
        var cut = targetPath.LastIndexOfAny(['/', '\\']);
        if (cut < 0)
        {
            return name;
        }

        var targetName = targetPath[(cut + 1)..];
        var folder = targetPath[..cut].TrimEnd('/', '\\');
        var folderCut = folder.LastIndexOfAny(['/', '\\']);
        var folderName = folderCut >= 0 ? folder[(folderCut + 1)..] : folder;
        if (folderName.Length == 0)
        {
            folderName = folder.Length > 0 ? folder : targetPath[..(cut + 1)];
        }

        return string.Equals(targetName, name, StringComparison.OrdinalIgnoreCase)
            ? $"{name} to {folderName}"
            : $"{name} to {folderName}{targetPath[cut]}{targetName}";
    }

    private static bool IsOutcome(string line) =>
        line.StartsWith("finished", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("cancelled", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("started", StringComparison.OrdinalIgnoreCase);
}
