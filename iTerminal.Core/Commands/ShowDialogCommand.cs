using iConsole.Commands;
using iConsole.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Base for commands whose action is opening a pre-built dialog. Subclasses declare the key,
/// title, and description; override <see cref="OnBeforeShow"/> to seed the dialog from current
/// state, and <see cref="Command.CanExecute"/> to gate availability.
/// </summary>
public abstract class ShowDialogCommand<TDialog>(TDialog dialog) : Command where TDialog : Dialog
{
    protected TDialog Dialog => dialog;

    /// <summary>Called right before the dialog is shown.</summary>
    protected virtual void OnBeforeShow(TDialog dialog)
    {
    }

    public override async Task ExecuteAsync(object? args)
    {
        if (Control is null)
        {
            return;
        }

        OnBeforeShow(dialog);
        await Control.ShowDialogAsync(dialog);
    }
}
