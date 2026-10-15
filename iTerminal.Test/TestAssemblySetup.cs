using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Test;

/// <summary>
/// Raises the thread pool's floor before any test runs, as the core test assembly does. Each sandboxed app
/// leaves a few threads behind in the process (the driver's loops and the task manager's workers), and the
/// pool only grows by a thread or two per second once its minimum is in use: after a few dozen apps the
/// pool had no free thread for a new app's first <c>Task.Run</c>, and every later start waited out its
/// 20-second first-frame deadline. Nothing here changes what is tested; it only keeps the pool ready.
/// </summary>
[TestClass]
public static class TestAssemblySetup
{
    private const int MinimumThreads = 64;

    [AssemblyInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
        ThreadPool.SetMinThreads(
            Math.Max(workerThreads, MinimumThreads),
            Math.Max(completionPortThreads, MinimumThreads));
    }
}
