using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.PopUpWindows;
using iTerminal.Core.Helper;

namespace iTerminal.Core.Factories;

public class ProgressWindowFactory(
    IThemeService themeService,
    INotificationService? notificationService = null)
{
    /// <summary>
    /// The progress window for <paramref name="task"/> — and, since every operation the user starts
    /// and can walk away from comes through here, the notification that says how it ended.
    /// </summary>
    public ProgressWindow Create(BackgroundTask task)
    {
        if (task is not null)
        {
            TaskNotificationHelper.NotifyWhenFinished(task, notificationService);
        }

        return new ProgressWindow(
            task,
            themeService);
    }
}
