using iConsole.Commands;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Properties;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Space: adds up the size of the folder under the cursor — or of every marked folder — and
/// shows it in the size column in place of the file count. The walk runs on a background task, so
/// the key handler returns at once; the row says "…" until the total lands.
///
/// <para>Local disk only, never the ".." row, and not through links (<see cref="IFilePropertiesService.MeasureAsync"/>).
/// The figure lives on the row, so the next refresh of the folder — which rebuilds the rows —
/// takes it away, as in mc.</para>
/// </summary>
public class CalculateFolderSizeCommand(
    IContentModelService contentModelService,
    IContentModel? ownModel = null,
    IFilePropertiesService? propertiesService = null)
    : Command
{
    private readonly IFilePropertiesService _properties = propertiesService ?? new FilePropertiesService();

    public override string Title => "Folder size";
    public override string Description => "Calculate the size of the folder under the cursor (or of the marked folders).";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Spacebar, false, false, true);

    /// <summary>The last calculation started; a test waits for it instead of polling.</summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    private IContentModel? TargetModel
    {
        get
        {
            var focused = contentModelService.GetFocusedModel();
            if (ownModel is not null)
            {
                return ReferenceEquals(focused, ownModel) ? ownModel : null;
            }

            return focused ?? contentModelService.GetLastContentModel();
        }
    }

    private List<EntryBase> Folders()
    {
        var folders = new List<EntryBase>();
        var model = TargetModel;
        if (model is null)
        {
            return folders;
        }

        IEnumerable<IEntry> candidates = [];
        if (model is IMarkableContentModel marks && marks.GetMarkedEntries() is { Count: > 0 } marked)
        {
            candidates = marked;
        }
        else if (model.GetFocusedEntry() is { } focused)
        {
            candidates = [focused];
        }

        foreach (var entry in candidates)
        {
            if (entry is EntryBase row
                && !FocusedEntry.IsParentEntry(entry)
                && entry is not IDriveObjectEntry
                && entry.Url is { ContainerType: ContainerType.FileSystem, PathType: PathType.Folder })
            {
                folders.Add(row);
            }
        }

        return folders;
    }

    public override bool CanExecute() => Folders().Count > 0;

    public override Task ExecuteAsync(object? args)
    {
        var model = TargetModel;
        var folders = Folders();
        if (model is null || folders.Count == 0)
        {
            return Task.CompletedTask;
        }

        foreach (var entry in folders)
        {
            entry.FileSize = "…";
        }

        model.Update();
        PendingOperation = Task.Run(async () =>
        {
            foreach (var entry in folders)
            {
                try
                {
                    var measure = await _properties.MeasureAsync(Path.TrimEndingDirectorySeparator(entry.Url.Path));
                    entry.FileSize = measure.Bytes.ToFileSize() + (measure.Unreadable > 0 ? "+" : string.Empty);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    entry.FileSize = "?";
                }

                model.Update();
            }
        });
        return Task.CompletedTask;
    }
}
