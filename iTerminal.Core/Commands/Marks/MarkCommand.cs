using iConsole.Commands;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Marks;

/// <summary>
/// Base for the commands that mark rows in a file panel.
///
/// <para>Built two ways. Attached to a panel's browser it is given that browser's model, and acts
/// only while that model is the focused one: a browser offers a key the focused panel declined to
/// every other control too, so without the identity check the unfocused panel would mark its own
/// rows. Built for a menu it is given no model and acts on the panel the menu was opened over.</para>
/// </summary>
public abstract class MarkCommand(IContentModelService contentModelService, IContentModel? ownModel = null) : Command
{
    protected IContentModelService ContentModelService => contentModelService;

    /// <summary>The panel to mark in, or null when this command has nothing to act on.</summary>
    protected IContentModel? TargetModel
    {
        get
        {
            var model = ownModel is null
                ? contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel()
                : ReferenceEquals(contentModelService.GetFocusedModel(), ownModel) ? ownModel : null;
            return model is IMarkableContentModel { CanMark: true } ? model : null;
        }
    }

    protected IMarkableContentModel? Marks => TargetModel as IMarkableContentModel;

    public override bool CanExecute() => TargetModel is not null;

    /// <summary>Repaints the panel and its footer after the marks changed.</summary>
    protected static void Repaint(IContentModel model)
    {
        model.Update();
    }

    /// <summary>Moves the cursor <paramref name="step"/> rows, kept inside the listing and in view.</summary>
    protected static void MoveCursor(IContentModel model, int step)
    {
        var count = model.FilteredEntries.Count;
        if (count == 0)
        {
            return;
        }

        model.CursorPosition.Y = Math.Clamp(model.CursorPosition.Y + step, 0L, count - 1L);
        model.ScrollToVisible();
    }
}
