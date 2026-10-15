using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+F10: compare two files line by line (<see cref="FileDiffDialog"/>). Which two:
/// <list type="number">
/// <item>the focused panel's two marked files, when exactly two files are marked there;</item>
/// <item>otherwise the file under the cursor and the file under the other panel's cursor — mc's
/// way, and the one a user means with a file in each panel.</item>
/// </list>
/// Each file is read through the file system that serves its drive, resolved here while the panel
/// still has focus (a dialog takes it away), exactly as the checksum command does.
/// </summary>
public class DiffFilesDialogCommand(
    FileDiffDialog dialog,
    IContentModelService contentModelService,
    FileSystemResolver? fileSystemResolver = null)
    : ShowDialogCommand<FileDiffDialog>(dialog)
{
    public override string Title => "Compare files";
    public override string Description => "Show two files side by side with their differences: the cursor file against the other panel's, or two marked files.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F10, false, false, true);

    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    private IContentModel? OtherModel
    {
        get
        {
            var source = SourceModel;
            var otherName = source?.Name == ContentModelService.Left ? ContentModelService.Right : ContentModelService.Left;
            return contentModelService?.ContentModels.FirstOrDefault(m => m.Name == otherName);
        }
    }

    /// <summary>The two files to compare, or null when the panels offer no two files.</summary>
    internal (Url Left, Url Right, IContentModel LeftModel, IContentModel RightModel)? Pair()
    {
        var source = SourceModel;
        if (source is null)
        {
            return null;
        }

        if (source is IMarkableContentModel marks)
        {
            var marked = marks.GetMarkedEntries()
                .Where(e => !FocusedEntry.IsParentEntry(e) && e is not IDriveObjectEntry && e.Url is { PathType: PathType.File })
                .ToList();
            if (marked.Count == 2)
            {
                return (marked[0].Url, marked[1].Url, source, source);
            }
        }

        var other = OtherModel;
        if (other is null
            || FocusedEntry.ActionableUrl(source) is not { PathType: PathType.File } left
            || FocusedEntry.ActionableUrl(other) is not { PathType: PathType.File } right)
        {
            return null;
        }

        return (left, right, source, other);
    }

    public override bool CanExecute() => Pair() is not null;

    protected override void OnBeforeShow(FileDiffDialog dialog)
    {
        if (Pair() is not { } pair)
        {
            return;
        }

        dialog.Load(
            pair.Left, Resolve(pair.Left, pair.LeftModel),
            pair.Right, Resolve(pair.Right, pair.RightModel));
    }

    private IFileSystem? Resolve(Url url, IContentModel model)
    {
        if (url.ContainerType == ContainerType.FileSystem)
        {
            return null;
        }

        try
        {
            return fileSystemResolver?.Resolve(url, model, operation: "Compare");
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            // The dialog says so itself, when the read fails for want of a file system.
            return null;
        }
    }
}
