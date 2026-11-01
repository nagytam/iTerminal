using iConsole.Desktops;
using System.Diagnostics;

namespace iConsole.Jobs;

public abstract class Job : IJob
{
    public IDesktop? Desktop { get; set; }
    public virtual JobPriority Priority => JobPriority.Background;

    public virtual void OnInitialize(IDesktop desktop)
    {
        Desktop = desktop;
    }

    /// <summary>
    /// How long the job waits after the desktop starts it before its first pass. Zero (the default) starts at once.
    /// Use it for work nobody is waiting for at start-up — a scan, a network check, a server — so the first frame and
    /// the first keys are not competing with it. The wait is cancellable: shutdown during it ends the job without a pass.
    /// A job whose first pass must run at once (for example to show cached data) keeps the default.
    /// </summary>
    protected virtual TimeSpan InitialDelay => TimeSpan.Zero;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (InitialDelay > TimeSpan.Zero)
        {
            await Task.Delay(InitialDelay, cancellationToken);
        }

        OnStart();
        try
        {
            await RunAsync(cancellationToken);
        }
        catch 
        {
        }
        OnStop();
    }

    protected virtual void OnStart()
    {
        Debug.WriteLine($"Start job {GetType().FullName}");
    }

    protected virtual void OnStop()
    {
        Debug.WriteLine($"Stop job {GetType().FullName}");
    }

    public virtual void OnAfterStop()
    {
    }

    protected abstract Task RunAsync(CancellationToken cancellationToken);
}
