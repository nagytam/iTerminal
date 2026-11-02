using iConsole.Diagnostics;
using iTerminal.Core.AI;
using iTerminal.Core.Factories;
using iTerminal.Core.Services.RuntimeDependencies;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Core.Application;

/// <summary>
/// Builds the slowest independent singletons on their own threads while the host builds the rest of the object graph,
/// which is a chain of constructors on one thread and took about two seconds. The container locks each singleton while
/// it is built, so a service wanted by two threads is still built once, and the graph has no cycles to deadlock on:
/// whoever needs one of these first either builds it or waits for the thread that is.
/// </summary>
public static class StartupWarmUp
{
    private static readonly Type[] SlowServices =
    [
        typeof(IRuntimeDependencyService),
        typeof(IAiChatService),
        typeof(FilePanelFactory),
    ];

    /// <summary>
    /// Starts building the services and returns at once. A failure is left for the first real use to report. Call it once the
    /// password manager is unlocked: the AI client reads its key from the store, and a locked one answers "no secret".
    /// </summary>
    public static void Start(IServiceProvider services)
    {
        foreach (Type type in SlowServices)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    services.GetService(type);
                    StartupTrace.Mark("warmed-" + type.Name);
                }
                catch (Exception)
                {
                    // The same construction runs again, on the thread that needs it, and fails there with its own stack.
                }
            });
        }
    }
}
