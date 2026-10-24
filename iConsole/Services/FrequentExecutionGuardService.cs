using System.Diagnostics;

namespace iConsole.Services;

public class FrequentExecutionGuardService(IDateTimeProviderService dateTimeProviderService) : IFrequentExecutionGuardService
{
    private readonly IDateTimeProviderService _dateTimeProviderService = dateTimeProviderService;
    private DateTime LastExecution = DateTime.MinValue;
    private readonly TimeSpan FilterOffset = new(0, 0, 0, 0, 50);
    private Task? DelayExecutionTask = null;
    private Action? _pendingAction;
    private readonly Lock _delayGate = new();

    public void Execute(Action action)
    {
        var now = _dateTimeProviderService.Now;
        if (LastExecution.Add(FilterOffset).CompareTo(now) <= 0)
        {
            //Trace.TraceInformation("Guard: execute");
            LastExecution = now;
            action();
            return;
        }

        lock (_delayGate)
        {
            // The newest request wins. One that arrives while a run is already waiting replaces the waiting one, so the
            // last thing asked for is what runs; keeping the first and dropping the rest lost the latest update.
            _pendingAction = action;
            DelayExecutionTask ??= Task.Run(RunPending);
        }
    }

    private void RunPending()
    {
        while (true)
        {
            DateTime now;
            do
            {
                Thread.Sleep(FilterOffset);
                now = _dateTimeProviderService.Now;
            }
            while (LastExecution.Add(FilterOffset).CompareTo(now) > 0);

            Action? action;
            lock (_delayGate)
            {
                action = _pendingAction;
                _pendingAction = null;
                if (action is null)
                {
                    DelayExecutionTask = null;
                    return;
                }
            }

            LastExecution = _dateTimeProviderService.Now;
            // Trace.TraceInformation("Guard: execution after delay");
            try
            {
                action();
            }
            catch (Exception exception)
            {
                // One action failing must not stop the ones after it: they are still waiting to run.
                Debug.WriteLine($"{nameof(FrequentExecutionGuardService)} delayed action failed: {exception}");
            }
        }
    }
}
