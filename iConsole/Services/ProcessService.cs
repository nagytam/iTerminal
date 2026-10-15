using iConsole.Data;
using System.Diagnostics;

namespace iConsole.Services;

public class ProcessService
{
    public ConcurrentObservableCollection<Process> Processes { get; private set; }

    public ProcessService()
    {
        Processes = new (Process.GetProcesses().OrderBy(p => p.Id));
    }
}
