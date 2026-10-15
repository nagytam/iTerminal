using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class CreateFolderDialogCommand(CreateFolderDialog createFolderDialog, IContentModelService contentModelService)
    : ShowDialogCommand<CreateFolderDialog>(createFolderDialog)
{
    public override string Title => "Create folder";
    public override string Description => "Create a new folder in the current location.";

    // Shift+F7: plain F7 is Find files. Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F7, true, false, false);

    protected override void OnBeforeShow(CreateFolderDialog dialog)
    {
        dialog.BaseUrl = contentModelService.Url;
    }
}
