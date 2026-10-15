using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Music;

/// <summary>
/// Enter on a music panel. Folder rows navigate — into a control folder, an artist or an album —
/// and everything else is handed to the model, which knows whether the row means "play this",
/// "use this output" or "cycle the repeat mode".
///
/// <para>Registered ahead of <see cref="NavigateToFolderCommand"/> so it claims Enter on music
/// drives; on every other drive its <c>CanExecute</c> is false and the file-system command runs
/// exactly as before.</para>
/// </summary>
public class ActivateMusicEntryCommand(
    IContentModel contentModel,
    IContentModelService contentModelService)
    : MusicPanelCommand(contentModel, contentModelService)
{
    public override string Title => "Open or play";
    public override string Description => "Open the selected music folder, or run what the selected row stands for.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute() => base.CanExecute() && FocusedMusicEntry is not null;

    public override async Task ExecuteAsync(object? args)
    {
        var model = FocusedMusicModel;
        var entry = FocusedMusicEntry;
        if (model is null || entry is null)
        {
            return;
        }

        if (entry.Kind == MusicEntryKind.Folder)
        {
            await ContentModelService.SetUrlAsync(model.Name, entry.Url);
            return;
        }

        model.Activate(entry);
    }
}
