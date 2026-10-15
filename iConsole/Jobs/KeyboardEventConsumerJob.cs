using iConsole.Data;
using iConsole.Desktops;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Services;
using System.Diagnostics;

namespace iConsole.Jobs;

public class KeyboardEventConsumerJob(
    IBeepService beepService,
    HelpDialog helpDialog
    ) : Job
{
    private KeyboardEventPublisherJob _keyboardEventPublisherJob = null!;
    public override JobPriority Priority => JobPriority.Input;

    public override void OnInitialize(IDesktop desktop)
    {
        base.OnInitialize(desktop);
        _keyboardEventPublisherJob = Desktop?.Jobs?.FirstOrDefault(j => j is KeyboardEventPublisherJob) as KeyboardEventPublisherJob;
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _keyboardEventPublisherJob.InputAvailableSignal.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (_keyboardEventPublisherJob.KeyQueue.TryDequeue(out KeyEvent? keyEvent))
            {
                await HandleKeyEventAsync(keyEvent);
            }
        }
    }

    public async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = false;
        var modalControl = Desktop.GetModalChildControl();
        if (modalControl is not null)
        {
            isHandled = await modalControl.HandleKeyEventAsync(keyEvent);

            if (!isHandled && IsUnmodifiedHelpKey(keyEvent) && modalControl is not HelpDialog)
            {
                helpDialog.ContextControl = modalControl;
                helpDialog.ContextFocus = modalControl.GetFocusedControl() ?? modalControl;
                await modalControl.ShowDialogAsync(helpDialog);
                isHandled = true;
            }

            if (!isHandled)
            {
                Debug.WriteLine($"Unhandled key: {keyEvent.ConsoleKey}");
                beepService.Beep();
            }
        }

        // Off unless ITERMINAL_KEY_TRACE is set. Distinguishes "the binding did not match" from
        // "the terminal host swallowed the key and it never arrived".
        KeyTraceHelper.Trace(keyEvent, isHandled);
        return isHandled;
    }

    // Guarantees F1 opens context-sensitive help from anywhere, even inside dialogs that don't
    // carry their own HelpDialogCommand (e.g. GitClientDialog, SolutionViewerDialog).
    private static bool IsUnmodifiedHelpKey(KeyEvent keyEvent) =>
        keyEvent.ConsoleKey == ConsoleKey.F1 && !keyEvent.Shift && !keyEvent.Control && !keyEvent.Alt;
}
