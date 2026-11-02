using iConsole.Commands;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Contents;
using iConsole.Localization;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Listing;

/// <summary>
/// The Info listing: this panel shows the other panel's cursor entry as label/value rows, and is read-only while it
/// is on; Esc returns to the list. Palette only. Acts only on the panel's own browser and only while its model is
/// the focused one.
/// </summary>
public class ToggleInfoListingCommand(IContentModelService contentModelService, ContentBrowser browser) : Command
{
    public override string Title => UiText.Get("Listing.InfoTitle", "Info listing");
    public override string Description => UiText.Get(
        "Listing.InfoDescription",
        "Shows the other panel's cursor entry. Keys act on this panel, whose cursor is hidden while this mode is on. The mouse is ignored. Esc returns to the list.");

    public override bool CanExecute() =>
        browser.ContentModel is not null
        && browser.ContentModel is not ContentModelPreview
        && ReferenceEquals(contentModelService.GetFocusedModel(), browser.ContentModel);

    public override Task ExecuteAsync(object? args)
    {
        if (CanExecute())
        {
            browser.ToggleInfoListing();
        }

        return Task.CompletedTask;
    }
}
