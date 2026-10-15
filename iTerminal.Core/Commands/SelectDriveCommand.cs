using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class SelectDriveCommand(
    IContentModelService contentModelService
    ) : Command
{
    public override string Title => "Select drive";
    public override string Description => "Go to the drive under the cursor.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        var selectedDrive = (Control?.Parent as FolderSelectorDrivesTabPage)?.SelectedDrive;
        return base.CanExecute() && selectedDrive is not null && selectedDrive.IsEnabled;
    }

    public override async Task ExecuteAsync(object? arg)
    {
        var selectedDrive = (Control?.Parent as FolderSelectorDrivesTabPage)?.SelectedDrive;
        if (selectedDrive is null)
        {
            return;
        }
        var selectedContentModel = contentModelService.GetLastContentModel();
        if (selectedContentModel is not null)
        {
            var selectedDriveUrl = new Url(selectedDrive.DriveInfo?.Name ?? selectedDrive.Url.FullUrl);
            await contentModelService.SetUrlAsync(selectedContentModel.Name, selectedDriveUrl);
        }
        CloseContainingDialog();
    }
}
