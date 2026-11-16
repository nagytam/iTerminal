using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+P: puts the path of the entry under the cursor on the text clipboard, to paste into a terminal, an
/// editor or a mail. A local file gives its path; any other drive gives its full address. The ".." row and an empty
/// folder have nothing to copy and decline.
/// </summary>
public class CopyPathCommand(IContentModelService contentModelService, INotificationService? notificationService = null) : Command
{
    public override string Title => UiText.Title("Copy the path of the entry");
    public override string Description => UiText.Describe("Put the path of the entry under the cursor on the clipboard, to paste it elsewhere.");

    public override Key? Key { get; init; } = new Key(ConsoleKey.P, true, false, true);

    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    private Url? SourceUrl => FocusedEntry.ActionableUrl(SourceModel);

    public override bool CanExecute() => SourceUrl is not null;

    public override async Task ExecuteAsync(object? args)
    {
        if (SourceUrl is not { } url)
        {
            return;
        }

        string text = url.ContainerType == ContainerType.FileSystem ? url.Path : url.FullUrl;
        await TextClipboard.Current.SetTextAsync(text);
        notificationService?.Notify(
            UiText.Get("Notify.PathCopiedTitle", "Path copied"),
            text,
            NotificationSeverity.Success);
    }
}
