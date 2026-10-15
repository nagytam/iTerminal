using iConsole.Desktops;

namespace iConsole.Jobs;

public interface IJob
{
    IDesktop Desktop { set; }
    JobPriority Priority { get; }
    void OnInitialize(IDesktop desktop);
    Task ExecuteAsync(CancellationToken token);
    void OnAfterStop();
}