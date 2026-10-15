namespace iConsole.Tasks;

public interface IBackgroundTask
{
    string Name { get; }
    string DisplayName { get; }
    TaskPriority Priority { get; }
    CancellationTokenSource CancellationTokenSource { get; }
    Task RunAsync();
}