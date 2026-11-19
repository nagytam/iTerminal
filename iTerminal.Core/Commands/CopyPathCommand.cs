using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+Insert: puts the path of the entry under the cursor on the text clipboard, to paste into a terminal, an
/// editor or a mail. A local file gives its path; any other drive gives its full address. The ".." row and an empty
/// folder have nothing to copy and decline.
/// </summary>
public class CopyPathCommand(IContentModelService contentModelService, INotificationService? notificationService = null) : Command
{
    public override string Title => UiText.Title("Copy the path of the entry");
    public override string Description => UiText.Describe("Put the path of the entry under the cursor on the clipboard, to paste it elsewhere.");

    // Ctrl+Shift+P is the Windows Terminal and VS Code command palette, and Ctrl+Shift+letter chords
    // often send the same byte as Ctrl+letter, so Ctrl+Shift+Insert is the chord that reaches the app.
    public override Key? Key { get; init; } = new Key(ConsoleKey.Insert, true, false, true);

    /// <summary>The text put on the clipboard for an entry: its path, or its full address on another drive.</summary>
    protected virtual string TextFor(Url url) =>
        url.ContainerType == ContainerType.FileSystem ? url.Path : url.FullUrl;

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

        string text = TextFor(url);
        await TextClipboard.Current.SetTextAsync(text);
        notificationService?.Notify(
            UiText.Get("Notify.PathCopiedTitle", "Path copied"),
            text,
            NotificationSeverity.Success);
    }
}
