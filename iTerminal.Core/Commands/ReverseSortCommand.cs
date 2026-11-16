using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+U: flips the sort direction of the focused panel, and saves it as the Sort dialog does. The "as listed"
/// order has no direction, so from there the first press starts a name sort, and the next flips it.
/// </summary>
public class ReverseSortCommand(IContentModelService contentModelService, ISettingsService settingsService) : Command
{
    public override string Title => UiText.Title("Reverse the sort order");
    public override string Description => UiText.Describe("Flip the sort direction of the focused panel, saved for the next start-up.");

    public override Key? Key { get; init; } = new Key(ConsoleKey.U, true, false, true);

    private IContentModel? Source => contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    public override bool CanExecute() => Source is ISortableContentModel { CanSort: true };

    public override Task ExecuteAsync(object? args)
    {
        if (Source is not ISortableContentModel sortable || !sortable.CanSort)
        {
            return Task.CompletedTask;
        }

        PanelSort current = sortable.Sort;
        PanelSort next = current.Mode == PanelSortMode.AsListed
            ? new PanelSort(PanelSortMode.Name)
            : current with { Descending = !current.Descending };

        sortable.SetSort(next);
        settingsService.Set(PanelSort.SettingsKey(Source?.Name ?? string.Empty), next.Serialize());
        return Task.CompletedTask;
    }
}
