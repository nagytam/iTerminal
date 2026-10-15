using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Commands;

public class NavigateToParentFolderCommand(
    IContentModel contentModel,
    IContentModelService contentModelService
    ) : Command
{
    public override string Title => "Parent folder";
    public override string Description => "Go to the parent folder of the current location.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.PageUp, shift: false, alt: false, control: true);

    public override async Task ExecuteAsync(object? arg)
    {
        await contentModelService.SetUrlAsync(contentModel.Name, contentModel.Url.ParentUrl);
    }
}
