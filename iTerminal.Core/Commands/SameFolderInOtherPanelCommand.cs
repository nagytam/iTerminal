using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Controls.Contents;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+D: the other file panel goes to the folder this one shows, for comparing or copying between two views of
/// the same place. The keyboard twin of the AI tool <c>open_same_folder_in_other_panel</c>. It declines when the other
/// panel is not there (a panel that is hidden by zoom is still there).
/// </summary>
public class SameFolderInOtherPanelCommand(IContentModelService contentModelService) : Command
{
    public override string Title => UiText.Title("Same folder in the other panel");
    public override string Description => UiText.Describe("Show the folder of the focused panel in the other panel too.");

    public override Key? Key { get; init; } = new Key(ConsoleKey.D, true, false, true);

    private IContentModel? Source => contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    private IContentModel? Other
    {
        get
        {
            if (Source is not { } source)
            {
                return null;
            }

            string otherName = source.Name == Content.Left ? Content.Right : Content.Left;
            return contentModelService.ContentModels.FirstOrDefault(model => model.Name == otherName);
        }
    }

    public override bool CanExecute() => Source?.Url is not null && Other is not null;

    public override async Task ExecuteAsync(object? args)
    {
        if (Source?.Url is not { } url || Other is not { } other)
        {
            return;
        }

        await contentModelService.SetUrlAsync(other.Name, url);
    }
}
