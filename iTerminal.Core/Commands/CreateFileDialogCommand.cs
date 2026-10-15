using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class CreateFileDialogCommand(CreateFileDialog createFileDialog, IContentModelService contentModelService)
    : ShowDialogCommand<CreateFileDialog>(createFileDialog)
{
    public override string Title => "Create file";
    public override string Description => "Create a new, empty file in the current folder.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F4, true, false, false);

    protected override void OnBeforeShow(CreateFileDialog dialog)
    {
        dialog.BaseUrl = contentModelService.Url;
    }
}
