using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test;

/// <summary>
/// Raises the thread pool's floor before any test runs. MSTest runs every <c>[Timeout]</c> test on a
/// pool thread, and much of the code under test hands work to <c>Task.Run</c>; the pool only grows
/// by a thread or two per second once its minimum is in use. With the suite running in parallel —
/// and <c>dotnet test iTerminal.slnx</c> running the test projects side by side — trivial tests used
/// to miss a 2-second timeout before their body had even started, and background refreshes waited
/// well past the deadlines the tests poll against. Nothing here changes what is tested; it only
/// takes the pool's warm-up out of the measured time.
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
