using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Commands;

/// <summary>
/// Points the active panel at whatever has been typed into the folder selector's Path box. This is
/// the one entry point in the selector that reaches every drive kind the app has, so it does no
/// scheme handling of its own beyond trimming: <see cref="Url"/> decides what the text means, and a
/// scheme added to the app works here the day it is added.
/// </summary>
public class GoToPathCommand(
    IContentModelService contentModelService,
    FolderUrlValidationModel pathValidator) : Command
{
    public override string Title => "Go to path";
    public override string Description => "Point the active panel at the typed path or URL.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        return base.CanExecute()
            && (Control?.Parent is FolderSelectorPathTabPage)
            && pathValidator.EnsureCurrent();
    }

    public override async Task ExecuteAsync(object? arg)
    {
        if (Control?.Parent is not FolderSelectorPathTabPage pathTabPage)
        {
            return;
        }

        var path = pathTabPage.Path.Trim();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await contentModelService.SetUrlAsync(new Url(path));
        pathTabPage.RememberPath(path);
        CloseContainingDialog();
    }
}
