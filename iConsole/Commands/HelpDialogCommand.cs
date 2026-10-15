using iConsole.Data;
using iConsole.Dialogs;

namespace iConsole.Commands;

public class HelpDialogCommand(HelpDialog helpDialog) : Command
{
    private HelpDialog _helpDialog { get; init; } = helpDialog;

    public override string Title => "Help";
    public override string Description => "Show the commands available in the current context.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F1, false, false, false);

    public override async Task ExecuteAsync(object? arg)
    {
        if (Control is null) return;
        _helpDialog.ContextControl = Control.GetRootControl();
        _helpDialog.ContextFocus = Control.GetRootControl().GetFocusedControl() ?? Control;
        var dialog = _helpDialog;
        await Control.ShowDialogAsync(dialog);
    }
}
