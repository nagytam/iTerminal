using System.Diagnostics;

namespace iConsole.Services;

public class FrequentExecutionGuardService(IDateTimeProviderService dateTimeProviderService) : IFrequentExecutionGuardService
{
    private readonly IDateTimeProviderService _dateTimeProviderService = dateTimeProviderService;
    private DateTime LastExecution = DateTime.MinValue;
    private readonly TimeSpan FilterOffset = new(0, 0, 0, 0, 50);
    private Task? DelayExecutionTask = null;

    public void Execute(Action action)
    {
        var now = _dateTimeProviderService.Now;
        if (LastExecution.Add(FilterOffset).CompareTo(now) <= 0)
        {
            //Trace.TraceInformation("Guard: execute");
            LastExecution = now;
            action();
        }
        else
        {
            //Trace.TraceInformation("Guard: delay execution");
            DelayExecutionTask ??= Task.Run(() =>
                {
                    DateTime now;
                    do
                    {
                        Thread.Sleep(FilterOffset);
                        now = _dateTimeProviderService.Now;
                    }
                    while (LastExecution.Add(FilterOffset).CompareTo(now) > 0);
                    now = _dateTimeProviderService.Now;
                    LastExecution = now;
                    // Trace.TraceInformation("Guard: execution after delay");
                    action();
                    DelayExecutionTask = null;
                });
        }
    }
}
