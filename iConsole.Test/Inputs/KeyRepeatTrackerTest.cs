using iConsole.Data;
using iConsole.Inputs;

namespace iConsole.Test.Inputs;

[TestClass]
public class KeyRepeatTrackerTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void OnlyAKeyDownOfAKeyThatIsAlreadyDown_IsARepeat()
    {
        var tracker = new KeyRepeatTracker();

        Assert.IsFalse(tracker.Register(65, keyDown: true));
        Assert.IsTrue(tracker.Register(65, keyDown: true));
        Assert.IsFalse(tracker.Register(65, keyDown: false), "a release is never a repeat");
        Assert.IsFalse(tracker.Register(65, keyDown: true));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AReleaseOfAKeyThatWasNeverDown_IsHarmless()
    {
        var tracker = new KeyRepeatTracker();

        Assert.IsFalse(tracker.Register(99, keyDown: false));
        Assert.IsFalse(tracker.Register(99, keyDown: true));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(-5, 1)]
    [DataRow(0, 1)]
    [DataRow(3, 3)]
    [DataRow(int.MaxValue, KeyRepeatTracker.MaxRepeatCount)]
    public void CountFor_IsAtLeastOneAndAtMostTheLimit(int count, int expected)
    {
        Assert.AreEqual(expected, KeyRepeatTracker.CountFor(count));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AKeyEvent_IsNotARepeatUnlessTheSourceSaysSo_AndRepeatDoesNotChangeWhatItMeans()
    {
        var plain = new KeyEvent('a', ConsoleKey.A, false, false, false);
        var repeat = new KeyEvent('a', ConsoleKey.A, false, false, false) { IsRepeat = true };

        Assert.IsFalse(plain.IsRepeat);
        Assert.IsTrue(repeat.IsRepeat);
        Assert.IsTrue(plain.IsIdentical(repeat));
    }
}
