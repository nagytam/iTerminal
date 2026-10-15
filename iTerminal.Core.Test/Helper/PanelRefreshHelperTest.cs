using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Helper;

[TestClass]
public class PanelRefreshHelperTest
{
    // The refresh runs inline on the thread that finished the task, so by the time RunAsync has
    // returned it has either happened or it never will — nothing here has to wait for it.
    private static readonly TimeSpan Immediately = TimeSpan.Zero;

    /// <summary>A task whose outcome the test decides, driven through the real
    /// <see cref="BackgroundTask.RunAsync"/> so the status transition is the production one.</summary>
    private sealed class FakeTask(string? error = null, bool cancel = false) : BackgroundTask
    {
        public override Task ExecuteAsync()
        {
            if (cancel)
            {
                CancellationTokenSource.Cancel();
            }
            Error = error;
            return Task.CompletedTask;
        }
    }

    /// <summary>A local path in the platform's own separator — <see cref="Url.ParentUrl"/> cuts a
    /// <see cref="ContainerType.FileSystem"/> url at <see cref="Path.DirectorySeparatorChar"/>.</summary>
    private static string LocalPath(params string[] parts) =>
        string.Join(Path.DirectorySeparatorChar, parts);

    private static IContentModelService CreateService(params PanelStub[] panels)
    {
        var contentModels = new ConcurrentList<IContentModel>();
        foreach (var panel in panels)
        {
            contentModels.Add(panel.Object);
        }

        var service = new Mock<IContentModelService>(MockBehavior.Loose);
        service.Setup(s => s.ContentModels).Returns(contentModels);
        return service.Object;
    }

    // ── the folder a panel is showing ────────────────────────────────────────

    [TestMethod]
    public async Task RefreshWhenFinished_RefreshesThePanelShowingTheTargetFolder()
    {
        var target = new PanelStub(LocalPath("C:", "tmp", "target") + Path.DirectorySeparatorChar);
        var elsewhere = new PanelStub(LocalPath("C:", "tmp", "elsewhere"));
        var task = new FakeTask();

        PanelRefreshHelper.RefreshWhenFinished(
            task, CreateService(target, elsewhere), new Url(LocalPath("C:", "tmp", "target", "copied.txt")));
        await task.RunAsync();

        Assert.IsTrue(target.WasRefreshed(Immediately));
        Assert.AreEqual(true, target.WasForced, "A folder that just changed must not be served from the cache.");
        Assert.IsTrue(elsewhere.WasNotRefreshed(Immediately), "A panel showing an unrelated folder has nothing new to read.");
    }

    [TestMethod]
    public async Task RefreshWhenFinished_MatchesAPanelUrlWrittenWithoutItsTrailingSeparator()
    {
        // Url.ParentUrl drops the trailing separator; a panel's own Url usually keeps it. Comparing
        // the two literally would refresh nothing at all.
        var target = new PanelStub(LocalPath("C:", "tmp", "target"));
        var task = new FakeTask();

        PanelRefreshHelper.RefreshWhenFinished(
            task, CreateService(target), new Url(LocalPath("C:", "tmp", "target", "copied.txt")));
        await task.RunAsync();

        Assert.IsTrue(target.WasRefreshed(Immediately));
    }

    [TestMethod]
    public async Task RefreshWhenFinished_RefreshesBothPanelsOfACrossDriveMove()
    {
        var source = new PanelStub(LocalPath("C:", "tmp", "source"));
        var target = new PanelStub("memdrive:///d/");
        var task = new FakeTask();

        PanelRefreshHelper.RefreshWhenFinished(
            task,
            CreateService(source, target),
            new Url(LocalPath("C:", "tmp", "source", "moved.txt")),
            new Url("memdrive:///d/moved.txt"));
        await task.RunAsync();

        Assert.IsTrue(source.WasRefreshed(Immediately));
        Assert.IsTrue(target.WasRefreshed(Immediately));
    }

    [TestMethod]
    public async Task RefreshWhenFinished_ClampsTheCursorAgainstTheNewListing()
    {
        // Every drive but the local one re-lists synchronously inside Refresh, so this is where
        // deleting the last row stops leaving the cursor past the end of the listing.
        var target = new PanelStub(LocalPath("C:", "tmp", "target") + Path.DirectorySeparatorChar);
        var task = new FakeTask();

        PanelRefreshHelper.RefreshWhenFinished(
            task, CreateService(target), new Url(LocalPath("C:", "tmp", "target", "gone.txt")));
        await task.RunAsync();

        Assert.IsTrue(target.WasUpdated(Immediately));
    }

    // ── which outcomes trigger it ────────────────────────────────────────────

    [TestMethod]
    public void RefreshWhenFinished_DoesNothingWhileTheTaskIsStillRunning()
    {
        var target = new PanelStub(LocalPath("C:", "tmp", "target") + Path.DirectorySeparatorChar);
        var task = new FakeTask();

        PanelRefreshHelper.RefreshWhenFinished(
            task, CreateService(target), new Url(LocalPath("C:", "tmp", "target", "copied.txt")));

        Assert.IsTrue(target.WasNotRefreshed(Immediately));
    }

    [TestMethod]
    public async Task RefreshWhenFinished_SkipsAFailedRun()
    {
        // Nothing changed on the drive, and re-listing a remote folder costs a round trip.
        var target = new PanelStub("ssh://user@host:/d/");
        var task = new FakeTask(error: "Access to the path is denied.");

        PanelRefreshHelper.RefreshWhenFinished(
            task, CreateService(target), new Url("ssh://user@host:/d/copied.txt"));
        await task.RunAsync();

        Assert.IsTrue(target.WasNotRefreshed(Immediately));
    }

    [TestMethod]
    public async Task RefreshWhenFinished_StillRefreshesACancelledRun()
    {
        // A folder copy stopped half way through really did put files in the target.
        var target = new PanelStub(LocalPath("C:", "tmp", "target") + Path.DirectorySeparatorChar);
        var task = new FakeTask(cancel: true);

        PanelRefreshHelper.RefreshWhenFinished(
            task, CreateService(target), new Url(LocalPath("C:", "tmp", "target", "partial.txt")));
        await task.RunAsync();

        Assert.AreEqual(iConsole.Data.TaskStatus.Canceled, task.Status);
        Assert.IsTrue(target.WasRefreshed(Immediately));
    }

    // ── degenerate inputs ────────────────────────────────────────────────────

    [TestMethod]
    public async Task RefreshWhenFinished_WithoutAContentModelService_DoesNotThrow()
    {
        var task = new FakeTask();

        PanelRefreshHelper.RefreshWhenFinished(
            task, contentModelService: null, new Url(LocalPath("C:", "tmp", "target", "copied.txt")));

        await task.RunAsync();
    }

    [TestMethod]
    public async Task RefreshWhenFinished_WithNoUsableUrls_DoesNotThrow()
    {
        var target = new PanelStub(LocalPath("C:", "tmp", "target") + Path.DirectorySeparatorChar);
        var task = new FakeTask();

        PanelRefreshHelper.RefreshWhenFinished(task, CreateService(target), null, new Url(string.Empty));
        await task.RunAsync();

        Assert.IsTrue(target.WasNotRefreshed(Immediately));
    }

    [TestMethod]
    public async Task RefreshWhenFinished_WithAUrlWithNoParent_DoesNotThrow()
    {
        // Url.ParentUrl assumes a path well formed for its drive and throws on anything else. This
        // is computed on the key handler that pressed Enter, so it must not escape.
        var target = new PanelStub(LocalPath("C:", "tmp", "target"));
        var task = new FakeTask();

        PanelRefreshHelper.RefreshWhenFinished(task, CreateService(target), new Url("bare-name-with-no-separator"));
        await task.RunAsync();

        Assert.IsTrue(target.WasNotRefreshed(Immediately));
    }

    [TestMethod]
    public async Task RefreshWhenFinished_WhenOnePanelThrows_StillRefreshesTheOther()
    {
        // A dropped SSH connection must not stop the local panel from updating.
        var broken = new PanelStub(LocalPath("C:", "tmp", "target")).ThatFailsToRefresh(new IOException("connection lost"));
        var healthy = new PanelStub(LocalPath("C:", "tmp", "source"));
        var task = new FakeTask();

        PanelRefreshHelper.RefreshWhenFinished(
            task,
            CreateService(broken, healthy),
            new Url(LocalPath("C:", "tmp", "source", "moved.txt")),
            new Url(LocalPath("C:", "tmp", "target", "moved.txt")));
        await task.RunAsync();

        Assert.IsTrue(broken.WasRefreshed(Immediately));
        Assert.IsTrue(healthy.WasRefreshed(Immediately));
    }
}
