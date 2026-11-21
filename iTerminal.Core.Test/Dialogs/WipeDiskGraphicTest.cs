using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class WipeDiskGraphicTest
{
    [TestMethod]
    public void NothingDone_HeadAtTheStart_AndTheRestUntouched()
    {
        Assert.AreEqual("⠋ [▓░░░░░░░░░]", WipeDiskGraphic.Render(0, 10, tick: 0));
    }

    [TestMethod]
    public void Half_FillsHalfTheCells_WithTheHeadAtTheBoundary()
    {
        var line = WipeDiskGraphic.Render(0.5, 10, tick: 0);

        Assert.AreEqual("⠋ [█████▓░░░░]", line);
    }

    [TestMethod]
    public void Full_IsAllSolid_WithACheckMarkInFront()
    {
        Assert.AreEqual("✓ [██████████]", WipeDiskGraphic.Render(1, 10, tick: 999));
    }

    [TestMethod]
    public void Fraction_IsClampedAndNaNCountsAsNothing()
    {
        Assert.AreEqual(WipeDiskGraphic.Render(1, 4, 0), WipeDiskGraphic.Render(7, 4, 0));
        Assert.AreEqual(WipeDiskGraphic.Render(0, 4, 0), WipeDiskGraphic.Render(-3, 4, 0));
        Assert.AreEqual(WipeDiskGraphic.Render(0, 4, 0), WipeDiskGraphic.Render(double.NaN, 4, 0));
    }

    [TestMethod]
    public void TheHeadFlickers_AndTheSpinnerTurn_WithTheClock()
    {
        var first = WipeDiskGraphic.Render(0.3, 10, tick: 0);
        var later = WipeDiskGraphic.Render(0.3, 10, tick: WipeDiskGraphic.HeadMilliseconds);
        var spun = WipeDiskGraphic.Render(0.3, 10, tick: WipeDiskGraphic.SpinMilliseconds);

        Assert.AreNotEqual(first, later);
        Assert.AreNotEqual(first[0], spun[0]);
    }

    [TestMethod]
    public void ZeroCells_StillDrawsOne()
    {
        Assert.AreEqual(1, WipeDiskGraphic.Render(0, 0, 0).Count(c => c is '░' or '▓' or '▒' or '█'));
    }
}
