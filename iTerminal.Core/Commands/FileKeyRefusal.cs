using iConsole.Localization;
using iConsole.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// The notice a file key gives when it has nothing to act on: the cursor on ".." or on an empty folder, with nothing marked.
/// Copy, move, delete and rename used to do nothing at all then, and a key that does nothing without a word looks broken.
/// </summary>
public static class FileKeyRefusal
{
    public static void Notify(INotificationService? notificationService)
    {
        notificationService?.Notify(
            UiText.Get("Notice.NothingToAct.Title", "Nothing selected"),
            UiText.Get("Notice.NothingToAct.Message", "Put the cursor on a file or folder, or mark some, first."),
            NotificationSeverity.Info);
    }
}
