using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Commands;

public class NavigateToRootFolderCommand(IContentModel contentModel) : Command
{
    public override string Title => "Root folder";
    public override string Description => "Jump to the root of the current drive or container.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Oem5, false, false, true);

    public override Task ExecuteAsync(object? arg)
    {
        var url = contentModel.Url;
        var rootPath = url.RootUrl;
        contentModel?.SetUrl(rootPath);
        return Task.CompletedTask;
    }
}
