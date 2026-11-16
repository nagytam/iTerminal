using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+N: as <see cref="CopyPathCommand"/>, but the entry's name alone, for a file name in a rename, a mail or a
/// command. Declines on the same rows.
/// </summary>
public class CopyNameCommand(IContentModelService contentModelService, INotificationService? notificationService = null)
    : CopyPathCommand(contentModelService, notificationService)
{
    public override string Title => UiText.Title("Copy the name of the entry");
    public override string Description => UiText.Describe("Put the name of the entry under the cursor on the clipboard, without its folder.");

    public override Key? Key { get; init; } = new Key(ConsoleKey.N, true, false, true);

    protected override string TextFor(Url url) => url.Name;
}
