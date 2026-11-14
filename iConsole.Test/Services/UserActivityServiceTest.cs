using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class UserActivityServiceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void IdleFor_GrowsWithTheClock_FromTheMomentTheServiceWasMade()
    {
        var time = new VirtualTimeProvider();
        var activity = new UserActivityService(time);

        time.Advance(TimeSpan.FromMinutes(3));

        Assert.AreEqual(TimeSpan.FromMinutes(3), activity.IdleFor);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoteActivity_ResetsTheIdleTime()
    {
        var time = new VirtualTimeProvider();
        var activity = new UserActivityService(time);
        time.Advance(TimeSpan.FromMinutes(10));

        activity.NoteActivity();
        time.Advance(TimeSpan.FromSeconds(20));

        Assert.AreEqual(TimeSpan.FromSeconds(20), activity.IdleFor);
    }
}
