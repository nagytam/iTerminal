using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands;

public class NavigateToFolderCommand(
    IContentModel contentModel,
    IContentModelService contentModelService
    ) 
    : Command
{
    public override string Title => "Open folder";
    public override string Description => "Open the selected folder or follow the parent entry.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        var canExecute = base.CanExecute();
        var entry = contentModel.GetFocusedEntry();
        if (entry is null ||
            (
            (entry.Url.ContainerType == ContainerType.FileSystem) &&
            (entry.Url.PathType == PathType.File)
            // A search hit is a local file, but Enter on it means "go there", not "run it".
            && contentModel is not ContentModelSearchResults))
        {
            canExecute = false;
        }
        return canExecute;
    }

    public override async Task ExecuteAsync(object? arg)
    {
        if (contentModel is ContentModelFileSystem)
        {
            var name = contentModel.Name;
            var entry = contentModel.GetFocusedEntry();
            if (entry is not null)
            {
                if (entry.DisplayName == "..")
                {
                    var target = entry.Url;
                    await contentModelService.SetUrlAsync(name, target);
                }
                else
                {
                    if (contentModel.ExistsFolder(entry.Url))
                    {
                        var url = entry.FullName;
                        if (!url.EndsWith(Path.DirectorySeparatorChar))
                        {
                            url += Path.DirectorySeparatorChar;
                        }
                        await contentModelService.SetUrlAsync(name, url);
                    }
                }
            }
        }
        else if (contentModel is ContentModelSshDrive)
        {
            var name = contentModel.Name;
            var entry = contentModel.GetFocusedEntry();
            if (entry is not null)
            {
                var url = entry.Url;
                await contentModelService.SetUrlAsync(name, url);
            }
        }
        else if (contentModel is ContentModelFileSystemArchive)
        {
            var name = contentModel.Name;
            var entry = contentModel.GetFocusedEntry();
            contentModel = contentModelService.AddContentModel(contentModel.Name, entry!.Url)!;
            await contentModelService.SetUrlAsync(name, entry.Url);
            await contentModel.ContentBrowser.ChangeModelAsync(name);
        }
        else if (contentModel is ContentModelAsyncDrive)
        {
            // docker://, git:// and search://: a folder row is a URL to go to, wherever it points —
            // further into the drive, or (the root's "..") back out to the disk it was opened from.
            // A file row in search results is "show me where this is": its folder, cursor on it.
            var entry = contentModel.GetFocusedEntry();
            if (entry is null)
            {
                return;
            }

            if (entry.Url.PathType == PathType.Folder)
            {
                await contentModelService.SetUrlAsync(contentModel.Name, entry.Url);
            }
            else if (contentModel is ContentModelSearchResults && entry.Url.ContainerType == ContainerType.FileSystem)
            {
                await RevealAsync(contentModel.Name, entry.Url);
            }
        }
    }

    /// <summary>Opens the folder holding <paramref name="fileUrl"/> with the cursor on the file.</summary>
    private async Task RevealAsync(string panelName, Url fileUrl)
    {
        var folder = Path.GetDirectoryName(fileUrl.FullName);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        await contentModelService.SetUrlAsync(panelName, new Url(folder + Path.DirectorySeparatorChar));
        contentModelService.ContentModels.FirstOrDefault(m => m.Name == panelName)?.RestoreNavigation(fileUrl.FullUrl, 0, 0);
    }
}
