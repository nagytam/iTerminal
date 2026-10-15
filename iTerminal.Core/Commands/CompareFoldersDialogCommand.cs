using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

/// <summary>
/// Shift+F2: compare the two panels' folders (<see cref="CompareFoldersDialog"/>). Offered whenever
/// both panels show something whose rows can be marked — the music drives and the preview cannot.
/// </summary>
public class CompareFoldersDialogCommand(
    CompareFoldersDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<CompareFoldersDialog>(dialog)
{
    public override string Title => "Compare folders";
    public override string Description => "Compare the two panels' folders, list the differences and mark what to copy across.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F2, true, false, false);

    public override bool CanExecute() =>
        new[] { ContentModelService.Left, ContentModelService.Right }.All(name =>
            contentModelService.ContentModels.FirstOrDefault(m => m.Name == name) is IMarkableContentModel { CanMark: true });
}
