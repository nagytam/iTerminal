using iConsole.Commands;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Music;

/// <summary>
/// Base for the commands that only mean something while a music drive is focused. They are attached
/// to every content browser, like the file-system commands are, and stay dormant everywhere else.
///
/// <para>The focused model comes from <see cref="IContentModelService"/> rather than from the
/// browser's own field, and has to be the browser's own model. Both halves matter: a control tree
/// walk offers a key to the focused control first but then to <em>every</em> control, so without the
/// identity check a key the focused panel declined would be picked up by the other panel.</para>
/// </summary>
public abstract class MusicPanelCommand(
    IContentModel contentModel,
    IContentModelService contentModelService)
    : Command
{
    protected IContentModel ContentModel { get; } = contentModel;

    protected IContentModelService ContentModelService { get; } = contentModelService;

    /// <summary>The music model under the cursor, or null when this panel is not the focused music panel.</summary>
    protected IMusicContentModel? FocusedMusicModel
    {
        get
        {
            var focused = ContentModelService.GetFocusedModel();
            return focused is IMusicContentModel musicModel && ReferenceEquals(focused, ContentModel)
                ? musicModel
                : null;
        }
    }

    protected MusicEntry? FocusedMusicEntry => FocusedMusicModel?.GetFocusedEntry() as MusicEntry;

    public override bool CanExecute() => FocusedMusicModel is not null;
}
