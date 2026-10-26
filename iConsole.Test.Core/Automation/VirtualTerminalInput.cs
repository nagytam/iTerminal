using iConsole.Data;
using iConsole.Inputs;
using iConsole.Jobs;
using iConsole.Services;
using System.Collections.Concurrent;

namespace iConsole.Test.Core.Automation;

/// <summary>
/// A keyboard and mouse with nobody at them: the test hands it events and gets back a task that
/// completes once the application has handled each one.
///
/// <para>It is an <see cref="ITerminalInputSource"/>, so it plugs in where the Win32 or TTY
/// publisher would; events queued on <see cref="KeyQueue"/>/<see cref="MouseQueue"/> directly are
/// dispatched too, just without a completion to await.</para>
/// </summary>
public sealed class VirtualTerminalInput : ITerminalInputSource, IInputInjector
{
    private readonly ConcurrentQueue<PendingInput> _pending = new();

    public ConcurrentQueue<MouseEvent> MouseQueue { get; } = new();
    public ConcurrentQueue<KeyEvent> KeyQueue { get; } = new();
    public SemaphoreSlim InputAvailableSignal { get; } = new(0);

    /// <summary>Queues a key; the task says whether anything handled it.</summary>
    public Task<bool> SendKeyAsync(KeyEvent keyEvent) => Enqueue(new PendingInput(keyEvent, null));

    /// <summary>Queues a mouse event; the task says whether anything handled it.</summary>
    public Task<bool> SendMouseAsync(MouseEvent mouseEvent) => Enqueue(new PendingInput(null, mouseEvent));

    internal bool TryDequeuePending(out PendingInput pending) => _pending.TryDequeue(out pending!);

    private Task<bool> Enqueue(PendingInput pending)
    {
        _pending.Enqueue(pending);
        InputAvailableSignal.Release();
        return pending.Completion.Task;
    }

    internal sealed class PendingInput(KeyEvent? keyEvent, MouseEvent? mouseEvent)
    {
        public KeyEvent? KeyEvent { get; } = keyEvent;
        public MouseEvent? MouseEvent { get; } = mouseEvent;
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

/// <summary>
/// The input job for <see cref="VirtualTerminalInput"/>. It is the production
/// <see cref="TerminalInputConsumerJob"/> — same thread, same routing to the topmost modal, same
/// beep for an unhandled key — and only adds completing each event's task, so a test can wait for
/// the handler instead of sleeping. An exception thrown by a handler faults that event's task, which
/// fails the test that sent it rather than silently stopping the keyboard.
/// </summary>
public sealed class VirtualTerminalInputJob(VirtualTerminalInput input, IBeepService beepService, IToolTipService? toolTipService = null)
    : TerminalInputConsumerJob(input, beepService, toolTipService)
{
    private readonly VirtualTerminalInput _input = input;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _input.InputAvailableSignal.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            while (_input.MouseQueue.TryDequeue(out var mouseEvent))
            {
                await HandleMouseEventAsync(mouseEvent);
            }

            while (_input.KeyQueue.TryDequeue(out var keyEvent))
            {
                await HandleKeyEventAsync(keyEvent);
            }

            while (_input.TryDequeuePending(out var pending))
            {
                try
                {
                    var handled = pending.KeyEvent is not null
                        ? await HandleKeyEventAsync(pending.KeyEvent)
                        : await HandleMouseEventAsync(pending.MouseEvent!);
                    pending.Completion.TrySetResult(handled);
                }
                catch (Exception exception)
                {
                    pending.Completion.TrySetException(exception);
                }
            }
        }

        while (_input.TryDequeuePending(out var abandoned))
        {
            abandoned.Completion.TrySetCanceled(cancellationToken);
        }
    }
}
