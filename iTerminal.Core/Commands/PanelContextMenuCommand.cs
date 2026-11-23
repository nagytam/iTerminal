using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Contents;

namespace iTerminal.Core.Commands;

/// <summary>
/// The Menu key: opens the menu the right mouse button opens on a panel row — what can be done with the row under the cursor, with
/// the key of each — at that row. Shift+F10 stays the user menu and Shift+F1 the command palette, so the Edit menu and the palette
/// carry the same command for a terminal that never sends the Menu key.
///
/// Two instances exist. A panel's own is bound to the key and acts only while that panel is the focused one. The Edit menu's has no key
/// and no panel: it finds the panel of the focused model, or of the last one when a menu or dialog holds the focus.
/// </summary>
public class PanelContextMenuCommand(IContentModelService contentModelService, ContentBrowser? browser = null) : Command
{
    public override string Title => "Context menu";
    public override string Description => "Open the menu of what can be done with the entry under the cursor.";

    // Key ctor is (consoleKey, shift, alt, control). Only the panel's own instance listens to the key.
    public override Key? Key { get; init; } = browser is null ? null : new Key(ConsoleKey.Applications, false, false, false);

    private IContentModel? TargetModel => browser is not null
        ? ReferenceEquals(contentModelService.GetFocusedModel(), browser.ContentModel) ? browser.ContentModel : null
        : contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    private ContentBrowser? TargetBrowser => browser
        ?? contentModelService.ContentBrowsers.OfType<ContentBrowser>().FirstOrDefault(b => ReferenceEquals(b.ContentModel, TargetModel));

    /// <summary>
    /// Cheap, as it is asked on every draw: only whether there is a row the menu is offered on. It never asks a drive whether the
    /// row exists (<see cref="FocusedEntry.ActionableUrl"/> does); the menu itself comes out empty where it has nothing to offer.
    /// </summary>
    public override bool CanExecute() =>
        TargetModel?.GetFocusedEntry() is { } entry
        && !FocusedEntry.IsParentEntry(entry)
        && entry is not iTerminal.Core.Model.ContentModels.IDriveObjectEntry;

    public override async Task ExecuteAsync(object? args)
    {
        if (CanExecute() && TargetBrowser is { } target)
        {
            await target.ShowContextMenuAtCursorAsync();
        }
    }
}
