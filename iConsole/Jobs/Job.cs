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

    public async Task ExecuteAsync(CancellationToken token)
    {
        OnStart();
        try
        {
            await RunAsync(token);
        }
        catch 
        {
            // Deliberately ignored: the failure leaves nothing to undo, so the operation is best effort.
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
