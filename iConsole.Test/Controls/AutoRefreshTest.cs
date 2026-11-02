using iConsole.Controls;
using iConsole.Services;
using iConsole.Test.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Controls;

/// <summary>The periodic repaint a control starts for data that arrives on its own: it stops when told to, and starting it again replaces it.</summary>
[TestClass]
public class AutoRefreshTest
{
    private sealed class Repainting(IThemeService theme) : Control(theme)
    {
        public int Repaints { get; private set; }

        public override void Refresh() => Repaints++;

        public void Begin() => StartAutoRefresh();
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task StopAutoRefresh_EndsTheRepaints()
    {
        var control = new Repainting(new TestThemeService());
        control.Begin();
        await Task.Delay(350);
        Assert.IsTrue(control.Repaints > 0, "the repaint never started");

        control.StopAutoRefresh();
        await Task.Delay(100);
        var stoppedAt = control.Repaints;
        await Task.Delay(350);

        Assert.AreEqual(stoppedAt, control.Repaints, "a stopped control was still being repainted");
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task StartingAgain_ReplacesTheRepaint_AndOneStopEndsIt()
    {
        var control = new Repainting(new TestThemeService());
        control.Begin();
        control.Begin();
        await Task.Delay(350);

        control.StopAutoRefresh();
        await Task.Delay(100);
        var stoppedAt = control.Repaints;
        await Task.Delay(350);

        Assert.AreEqual(stoppedAt, control.Repaints, "the first repaint was left running beside the second");
    }
}
