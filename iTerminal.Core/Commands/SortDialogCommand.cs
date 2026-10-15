using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;

namespace iTerminal.Core.Commands;

/// <summary>
/// Shift+F11: choose the order of the focused panel's rows (<see cref="SortDialog"/>). Not offered
/// where the order is the content — the playing queue, a preview.
/// </summary>
public class SortDialogCommand(
    SortDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<SortDialog>(dialog)
{
    public override string Title => "Sort by…";
    public override string Description => "Order the panel's rows by name, extension, size or date, either way.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F11, true, false, false);

    /// <summary>The panel the key acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    public override bool CanExecute() => SourceModel is ISortableContentModel { CanSort: true };

    protected override void OnBeforeShow(SortDialog dialog)
    {
        // Handed over now, while the panel still has focus: the dialog takes it as it opens.
        dialog.Load(SourceModel);
    }
}
