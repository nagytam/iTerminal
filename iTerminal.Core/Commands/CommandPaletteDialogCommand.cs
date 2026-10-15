using iConsole.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.CommandPalette;

namespace iTerminal.Core.Commands;

/// <summary>
/// Shift+F1 — beside F1, which explains the keys — opens <see cref="CommandPaletteDialog"/>. The list
/// is gathered here, on the key handler, while the control the key came from still has focus: the
/// panel's commands can only say whether they apply while their panel is the focused one.
/// </summary>
public class CommandPaletteDialogCommand(CommandPaletteDialog dialog)
    : ShowDialogCommand<CommandPaletteDialog>(dialog)
{
    public override string Title => "Command palette";
    public override string Description => "Find any command by name and run it; its key is shown beside it.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F1, true, false, false);

    protected override void OnBeforeShow(CommandPaletteDialog dialog)
    {
        var root = Control?.GetRootControl();
        dialog.Load(root is null ? [] : CommandCatalog.Collect(root, exclude: command => command is CommandPaletteDialogCommand));
    }
}
