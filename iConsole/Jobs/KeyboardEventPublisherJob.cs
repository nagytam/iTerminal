using iConsole.Data;
using iConsole.Services;
using System.Collections.Concurrent;

namespace iConsole.Jobs;

public class KeyboardEventPublisherJob(ConsoleOwnership? consoleOwnership = null) : Job
{
    public ConcurrentQueue<KeyEvent> KeyQueue { get; } = new();
    public SemaphoreSlim InputAvailableSignal { get; } = new(0);
    public event EventHandler<KeyEvent>? KeyEvent;
    public override JobPriority Priority => JobPriority.Input;

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Lent to a child process: its keys are not this job's to read.
            if (consoleOwnership is not null && !consoleOwnership.TryBeginUse())
            {
                Thread.Sleep(10);
                continue;
            }

            try
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    var keyEvent = new KeyEvent(key.KeyChar, key.Key,
                        (key.Modifiers & ConsoleModifiers.Shift) == ConsoleModifiers.Shift,
                        (key.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt,
                        (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control
                        );
                    KeyQueue.Enqueue(keyEvent);
                    InputAvailableSignal.Release();
                    KeyEvent?.Invoke(this, keyEvent);
                }
            }
            finally
            {
                consoleOwnership?.EndUse();
            }

            Thread.Sleep(1);
        }
        return Task.CompletedTask;
    }
}
