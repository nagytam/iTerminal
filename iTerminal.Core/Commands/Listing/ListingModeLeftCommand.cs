using iConsole.Commands;
using iConsole.Data;
using iTerminal.Core.Controls.Contents;

namespace iTerminal.Core.Commands.Listing;

/// <summary>
/// Left in the Brief listing: one entry to the left within the cursor's line. Not claimed in the one-column
/// listing, so the key stays free there.
/// </summary>
public class ListingModeLeftCommand(ContentBrowser browser) : Command
{
    public override string Title => "Left in brief listing";
    public override string Description => "Move the cursor one column left within its row.";

    public override Key? Key { get; init; } = new Key(ConsoleKey.LeftArrow, false, false, false);

    public override bool CanExecute() => browser.IsBriefListing;

    public override Task ExecuteAsync(object? args)
    {
        browser.MoveWithinLine(-1);
        return Task.CompletedTask;
    }
}
