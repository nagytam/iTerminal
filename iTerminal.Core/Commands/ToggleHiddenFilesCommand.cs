using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+S: shows or hides hidden files and folders in both panels. The same setting as the Panels page, in one
/// press, saved for the next start-up and applied to every panel at once.
/// </summary>
public class ToggleHiddenFilesCommand(
    IContentModelService contentModelService,
    ISettingsService settingsService,
    INotificationService? notificationService = null) : Command
{
    public override string Title => UiText.Title("Show or hide hidden files");
    public override string Description => UiText.Describe("Show or hide hidden files and folders in both panels.");

    public override Key? Key { get; init; } = new Key(ConsoleKey.S, true, false, true);

    public override Task ExecuteAsync(object? args)
    {
        var next = PanelViewOptions.Load(settingsService) with { ShowHidden = !PanelViewOptions.Load(settingsService).ShowHidden };
        next.Save(settingsService);

        foreach (var model in contentModelService.ContentModels.AsSnapshot())
        {
            if (model is ISortableContentModel sortable)
            {
                sortable.SetViewOptions(next);
            }
        }

        notificationService?.Notify(
            UiText.Get("Notify.HiddenFilesTitle", "Hidden files"),
            next.ShowHidden
                ? UiText.Get("Notify.HiddenFilesShown", "Shown in both panels")
                : UiText.Get("Notify.HiddenFilesHidden", "Hidden in both panels"),
            NotificationSeverity.Info);
        return Task.CompletedTask;
    }
}
