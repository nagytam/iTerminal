using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands;

/// <summary>Edit > Filter by Label: narrows the focused local panel to the files with a colour or a tag (<see cref="LabelFilterDialog"/>).</summary>
public class LabelFilterDialogCommand(
    LabelFilterDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<LabelFilterDialog>(dialog)
{
    public override string Title => "Filter by Label";
    public override string Description => "Show only the files with a colour or a tag in the panel. Any colour and a blank tag show everything.";

    /// <summary>The panel the command acts on: the one focused, or the one the menu was opened over.</summary>
    private ContentModelBase? SourceModel =>
        (contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel()) as ContentModelBase;

    public override bool CanExecute() => SourceModel?.Url.ContainerType == ContainerType.FileSystem;

    protected override void OnBeforeShow(LabelFilterDialog dialog)
    {
        // Read now, while the panel still has focus: the dialog takes it as it opens.
        if (SourceModel is { } model)
        {
            dialog.Load(model);
        }
    }
}
