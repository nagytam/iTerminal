using iConsole.Commands;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Contents;
using iConsole.Localization;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Listing;

/// <summary>
/// The Long listing: the one-column row with an attributes column and no size column. Palette only: Ctrl+F is the
/// File menu's hot key. Acts only on the panel's own browser and only while its model is the focused one.
/// </summary>
public class ToggleLongListingCommand(IContentModelService contentModelService, ContentBrowser browser) : Command
{
    public override string Title => UiText.Get("Listing.LongTitle", "Long listing");
    public override string Description => UiText.Get("Listing.LongDescription", "Show each entry's attributes, date and time, with no size column, or back to the normal listing.");

    public override bool CanExecute() =>
        browser.ContentModel is not null
        && browser.ContentModel is not ContentModelPreview
        && ReferenceEquals(contentModelService.GetFocusedModel(), browser.ContentModel);

    public override Task ExecuteAsync(object? args)
    {
        if (CanExecute())
        {
            browser.ToggleLongListing();
        }

        return Task.CompletedTask;
    }
}
