using iTerminal.Core.Dialogs.Search;

namespace iTerminal.Core.Commands;

/// <summary>
/// Find Old Files: Find Files, opened on the files not changed for a year under the panel's folder. The name box starts empty
/// and the folder is the panel's, as with F7.
/// </summary>
public class FindOldFilesCommand(FindFilesDialog findFilesDialog)
    : ShowDialogCommand<FindFilesDialog>(findFilesDialog)
{
    /// <summary>The age an old file is, in days, when the command starts the search.</summary>
    public const int OldDays = 365;

    public override string Title => "Find Old Files";
    public override string Description => "Find the files not changed for a year under the panel's folder.";

    protected override void OnBeforeShow(FindFilesDialog dialog) => dialog.Preset(largerThanMegabytes: null, OldDays);
}
