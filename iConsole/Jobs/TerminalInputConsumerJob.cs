using iConsole.Data;
using iConsole.Inputs;
using iConsole.Services;
using System.Diagnostics;

namespace iConsole.Jobs;

/// <summary>
/// Drains <see cref="ITerminalInputSource"/> queues (mouse + keyboard) and dispatches to the modal control tree.
/// </summary>
public class TerminalInputConsumerJob(
    ITerminalInputSource inputSource,
    IBeepService beepService,
    IToolTipService? toolTipService = null
) : Job
{
    private readonly ITerminalInputSource _inputSource = inputSource;
    private readonly IToolTipService? _toolTipService = toolTipService;
    public override JobPriority Priority => JobPriority.Input;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _inputSource.InputAvailableSignal.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (_inputSource.MouseQueue.TryDequeue(out var mouseEvent)
                && mouseEvent is not null)
            {
                await DispatchAsync(() => HandleMouseEventAsync(mouseEvent));
            }

            while (_inputSource.KeyQueue.TryDequeue(out var keyEvent)
                && keyEvent is not null)
            {
                await DispatchAsync(() => HandleKeyEventAsync(keyEvent));
            }
        }
    }

    /// <summary>
    /// Runs one event's handler, containing anything it throws. This loop is the only thing reading
    /// the keyboard: an exception let out of it ends the job (<see cref="Job.ExecuteAsync"/> swallows
    /// it), and from then on every key is ignored — an app that still draws but no longer responds.
    /// Commands already contain their own exceptions; this covers a control's handler, which does not.
    /// </summary>
    private static async Task DispatchAsync(Func<Task<bool>> handle)
    {
        try
        {
            await handle();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Input handler failed: {exception}");
        }
    }

    public async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        // Tooltips follow the pointer; anything that is not a hover (a click, a drag, a wheel turn) hides them.
        if (mouseEvent.IsHover)
        {
            _toolTipService?.ReportPointer(mouseEvent.Position);
        }
        else
        {
            _toolTipService?.Dismiss();
        }

        var modalControl = Desktop?.GetModalChildControl();
        if (modalControl is null)
        {
            return false;
        }

        return await modalControl.HandleMouseEventAsync(mouseEvent);
    }

    public async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        _toolTipService?.Dismiss();

        var modalControl = Desktop?.GetModalChildControl();
        if (modalControl is null)
        {
            return false;
        }

        var isHandled = await modalControl.HandleKeyEventAsync(keyEvent);
        if (!isHandled)
        {
            Debug.WriteLine($"Unhandled key: {keyEvent.ConsoleKey}");
            beepService.Beep();
        }

        // Traced on both input paths, so the diagnostic does not depend on which driver is active.
        Helper.KeyTraceHelper.Trace(keyEvent, isHandled);
        return isHandled;
    }
}
