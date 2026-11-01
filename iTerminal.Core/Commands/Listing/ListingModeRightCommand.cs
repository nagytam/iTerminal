using iConsole.Commands;
using iConsole.Data;
using iTerminal.Core.Controls.Contents;

namespace iTerminal.Core.Commands.Listing;

/// <summary>
/// Right in the Brief listing: one entry to the right within the cursor's line. Not claimed in the one-column
/// listing, so the key stays free there.
/// </summary>
public class ListingModeRightCommand(ContentBrowser browser) : Command
{
    public override string Title => "Right in brief listing";
    public override string Description => "Move the cursor one column right within its row.";

    public override Key? Key { get; init; } = new Key(ConsoleKey.RightArrow, false, false, false);

    public override bool CanExecute() => browser.IsBriefListing;

    public override Task ExecuteAsync(object? args)
    {
        browser.MoveWithinLine(1);
        return Task.CompletedTask;
    }
}
