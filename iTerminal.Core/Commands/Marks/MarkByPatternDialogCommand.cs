using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands.Marks;

/// <summary>
/// Num+ / Num-: asks for a name pattern (<c>*.jpg;*.png</c>) and marks — or unmarks — every row
/// that matches. Bound to the numeric keypad's keys, which no file panel uses for anything else and
/// which is where every commander has always put them; the Edit menu reaches both without a keypad.
/// </summary>
public class MarkByPatternDialogCommand : ShowDialogCommand<MarkPatternDialog>
{
    private readonly IContentModelService _contentModelService;
    private readonly IContentModel? _ownModel;
    private readonly bool _marking;

    public MarkByPatternDialogCommand(
        MarkPatternDialog dialog,
        IContentModelService contentModelService,
        bool marking,
        IContentModel? ownModel = null)
        : base(dialog)
    {
        _contentModelService = contentModelService;
        _ownModel = ownModel;
        _marking = marking;
        Key = new Key(marking ? ConsoleKey.Add : ConsoleKey.Subtract, false, false, false);
    }

    public override string Title => _marking ? "Mark by pattern" : "Unmark by pattern";
    public override string Description => _marking
        ? "Mark the files whose names match a pattern."
        : "Unmark the files whose names match a pattern.";

    public override Key? Key { get; init; }

    private IContentModel? TargetModel
    {
        get
        {
            var model = _ownModel is null
                ? _contentModelService.GetFocusedModel() ?? _contentModelService.GetLastContentModel()
                : ReferenceEquals(_contentModelService.GetFocusedModel(), _ownModel) ? _ownModel : null;
            return model is IMarkableContentModel { CanMark: true } ? model : null;
        }
    }

    public override bool CanExecute() => TargetModel is not null;

    protected override void OnBeforeShow(MarkPatternDialog dialog)
    {
        dialog.Target = TargetModel;
        dialog.Marking = _marking;
    }
}
