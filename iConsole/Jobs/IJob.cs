using iConsole.Desktops;

namespace iConsole.Jobs;

public interface IJob
{
    #pragma warning disable S2376 // The host sets the desktop once and jobs read it through their own state; a getter would widen IJob.
    IDesktop Desktop { set; }
    #pragma warning restore S2376
    JobPriority Priority { get; }
    void OnInitialize(IDesktop desktop);
    Task ExecuteAsync(CancellationToken token);
    void OnAfterStop();
}