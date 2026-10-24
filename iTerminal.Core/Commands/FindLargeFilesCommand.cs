using iTerminal.Core.Dialogs.Search;

namespace iTerminal.Core.Commands;

/// <summary>
/// Find Large Files: Find Files, opened on the files at least 100 MB long under the panel's folder. The name box starts empty
/// and the folder is the panel's, as with F7.
/// </summary>
public class FindLargeFilesCommand(FindFilesDialog findFilesDialog)
    : ShowDialogCommand<FindFilesDialog>(findFilesDialog)
{
    /// <summary>The size a large file is, in megabytes, when the command starts the search.</summary>
    public const double LargeMegabytes = 100;

    public override string Title => "Find Large Files";
    public override string Description => "Find the files at least 100 MB long under the panel's folder.";

    protected override void OnBeforeShow(FindFilesDialog dialog) => dialog.Preset(LargeMegabytes, unchangedForDays: null);
}
