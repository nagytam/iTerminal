using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class NotificationServiceTest
{
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    [Timeout(60_000)]
    [TestMethod]
    public void Notify_ShowsTheNotification_AndRecordsIt()
    {
        var service = new NotificationService();

        var notification = service.Notify("Copy finished", "readme.txt", NotificationSeverity.Success);

        Assert.AreEqual("Copy finished", notification.Title);
        Assert.AreEqual("readme.txt", notification.Message);
        Assert.AreEqual(NotificationSeverity.Success, notification.Severity);
        CollectionAssert.AreEqual(new[] { notification }, service.Active.ToArray());
        CollectionAssert.AreEqual(new[] { notification }, service.History.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Notify_WithoutDuration_UsesTheSeveritysDefault_AndAnErrorStaysLongest()
    {
        var service = new NotificationService();

        var info = service.Notify("a");
        var error = service.Notify("b", severity: NotificationSeverity.Error);

        Assert.AreEqual(NotificationService.DefaultDuration(NotificationSeverity.Info), info.Duration);
        Assert.IsTrue(error.Duration > info.Duration);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Advance_RemovesANotificationOnceItsTimeIsUp_AndKeepsItInTheHistory()
    {
        var service = new NotificationService();
        var notification = service.Notify("a", duration: TimeSpan.FromSeconds(2));

        service.Advance(OneSecond);
        Assert.AreEqual(1, service.Active.Count, "half way");

        service.Advance(OneSecond);
        Assert.AreEqual(0, service.Active.Count);
        CollectionAssert.AreEqual(new[] { notification }, service.History.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Advance_NeverExpiresANotificationWithAnInfiniteDuration()
    {
        var service = new NotificationService();
        var sticky = service.Notify("a", duration: Timeout.InfiniteTimeSpan);

        service.Advance(TimeSpan.FromHours(1));

        CollectionAssert.AreEqual(new[] { sticky }, service.Active.ToArray());
        Assert.IsTrue(service.Dismiss(sticky.Id));
        Assert.AreEqual(0, service.Active.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Notify_BeyondMaxVisible_Waits_AndItsTimeStartsWhenItIsShown()
    {
        var service = new NotificationService { MaxVisible = 2 };
        var first = service.Notify("1", duration: TimeSpan.FromSeconds(1));
        var second = service.Notify("2", duration: TimeSpan.FromSeconds(5));
        var third = service.Notify("3", duration: TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(new[] { first, second }, service.Active.ToArray());

        // The first expires and the third takes its place — with its two seconds still whole.
        service.Advance(OneSecond);
        CollectionAssert.AreEqual(new[] { second, third }, service.Active.ToArray());

        service.Advance(OneSecond);
        CollectionAssert.AreEqual(new[] { second, third }, service.Active.ToArray());

        service.Advance(OneSecond);
        CollectionAssert.AreEqual(new[] { second }, service.Active.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Notify_TheSameAsOneOnScreen_RestartsItsTime_InsteadOfStackingACopy()
    {
        var service = new NotificationService();
        var first = service.Notify("Saved", duration: TimeSpan.FromSeconds(2));
        service.Advance(OneSecond);

        var again = service.Notify("Saved", duration: TimeSpan.FromSeconds(2));

        Assert.AreSame(first, again);
        Assert.AreEqual(1, service.Active.Count);
        Assert.AreEqual(1, service.History.Count);

        service.Advance(OneSecond);
        Assert.AreEqual(1, service.Active.Count, "the second post gave it its two seconds back");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Dismiss_RemovesAnActiveOrAWaitingNotification_AndShowsTheNextOne()
    {
        var service = new NotificationService { MaxVisible = 1 };
        var first = service.Notify("1");
        var second = service.Notify("2");
        var third = service.Notify("3");

        Assert.IsTrue(service.Dismiss(second.Id), "still waiting");
        Assert.IsTrue(service.Dismiss(first.Id));
        Assert.IsFalse(service.Dismiss(first.Id), "already gone");

        CollectionAssert.AreEqual(new[] { third }, service.Active.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DismissAll_ClearsTheScreenAndTheQueue_ButNotTheHistory()
    {
        var service = new NotificationService { MaxVisible = 1 };
        service.Notify("1");
        service.Notify("2");

        service.DismissAll();
        service.Advance(OneSecond);

        Assert.AreEqual(0, service.Active.Count);
        Assert.AreEqual(2, service.History.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void History_IsCapped_KeepingTheNewest()
    {
        var service = new NotificationService { MaxHistory = 3 };
        for (var i = 1; i <= 5; i++)
        {
            service.Notify(i.ToString());
        }

        CollectionAssert.AreEqual(new[] { "3", "4", "5" }, service.History.Select(n => n.Title).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Version_AndChanged_FollowWhatIsOnScreen_NotWhatIsWaiting()
    {
        var service = new NotificationService { MaxVisible = 1 };
        var changes = 0;
        service.Changed += (_, _) => changes++;

        var shown = service.Notify("1", duration: TimeSpan.FromSeconds(1));
        Assert.AreEqual(1, changes);
        var version = service.Version;

        service.Notify("2");
        Assert.AreEqual(1, changes, "a notification that only joined the queue changed nothing on screen");
        Assert.AreEqual(version, service.Version);

        service.Advance(TimeSpan.FromMilliseconds(100));
        Assert.AreEqual(1, changes, "nothing expired");

        service.Advance(OneSecond);
        Assert.AreEqual(2, changes);
        Assert.AreNotEqual(version, service.Version);
        Assert.IsFalse(service.Active.Contains(shown));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Notify_ASubscriberThatThrows_DoesNotFailThePoster()
    {
        var service = new NotificationService();
        service.Changed += (_, _) => throw new InvalidOperationException();

        service.Notify("a");

        Assert.AreEqual(1, service.Active.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Notify_FromManyThreads_LosesNothing()
    {
        var service = new NotificationService { MaxVisible = 1000, MaxHistory = 1000 };

        Parallel.For(0, 500, i => service.Notify(i.ToString()));

        Assert.AreEqual(500, service.Active.Count);
        Assert.AreEqual(500, service.Active.Select(n => n.Id).Distinct().Count());
    }
}
