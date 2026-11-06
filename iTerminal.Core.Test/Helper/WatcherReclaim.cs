namespace iTerminal.Core.Test.Helper;

/// <summary>
/// A file panel that a test never closes keeps its <c>FileSystemWatcher</c> (an inotify instance on Linux) until the
/// garbage collector finalises it. The parallel tests leave many behind, and Linux allows 128 instances per user, so a
/// serial class that runs after them could be refused a watcher. Its class initialiser calls this to release them first.
/// </summary>
internal static class WatcherReclaim
{
    public static void ReleaseAbandonedWatchers()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
