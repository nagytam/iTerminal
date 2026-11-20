using iConsole.Display;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Display;

[TestClass]
public class TimedAnimationTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Step_DividesTheRunIntoEqualStepsAndEndsAtDone()
    {
        var animation = new TimedAnimation(150, 3);
        animation.Start(1_000);

        Assert.AreEqual(0, animation.Step(1_000));
        Assert.AreEqual(1, animation.Step(1_050));
        Assert.AreEqual(2, animation.Step(1_100));
        Assert.AreEqual(2, animation.Step(1_149));
        Assert.AreEqual(3, animation.Step(1_150), "The step is StepCount once the run has passed its duration.");
        Assert.IsFalse(animation.IsRunning, "A finished run stops itself.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Step_IsDoneWhenNotRunning()
    {
        var animation = new TimedAnimation(150, 3);

        Assert.AreEqual(3, animation.Step(0));
        Assert.IsFalse(animation.IsRunning);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Start_AtClockZero_IsRunning()
    {
        var animation = new TimedAnimation(150, 3);
        animation.Start(0);

        Assert.IsTrue(animation.IsRunning, "A zero clock reading is a valid start, so the flag, not the time, says it runs.");
        Assert.AreEqual(0, animation.Step(0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Stop_EndsTheRunAtOnce()
    {
        var animation = new TimedAnimation(150, 3);
        animation.Start(0);

        animation.Stop();

        Assert.IsFalse(animation.IsRunning);
        Assert.AreEqual(3, animation.Step(10));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Start_ReplacesARunAlreadyInProgress()
    {
        var animation = new TimedAnimation(150, 3);
        animation.Start(0);

        animation.Start(100);

        Assert.AreEqual(0, animation.Step(100), "The second start restarts the steps from its own time.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_RejectsAnEmptyRunOrNoSteps()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TimedAnimation(0, 3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TimedAnimation(150, 0));
    }
}
