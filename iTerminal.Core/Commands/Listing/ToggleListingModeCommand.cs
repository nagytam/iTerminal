using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Listing;

/// <summary>
/// Ctrl+F: switches the panel between its one-column listing and the Brief listing (several name columns).
/// Acts only on the panel's own browser and only while its model is the focused one, because a browser
/// offers a key the focused panel declined to every other control too.
/// </summary>
public class ToggleListingModeCommand(IContentModelService contentModelService, ContentBrowser browser) : Command
{
    public override string Title => "Brief listing";
    public override string Description => "Show the entries in several name columns, or back to one column.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } = new Key(ConsoleKey.F, false, false, true);

    public override bool CanExecute() =>
        browser.ContentModel is not null
        && browser.ContentModel is not ContentModelPreview
        && ReferenceEquals(contentModelService.GetFocusedModel(), browser.ContentModel);

    public override Task ExecuteAsync(object? args)
    {
        if (CanExecute())
        {
            browser.ToggleBriefListing();
        }

        return Task.CompletedTask;
    }
}
