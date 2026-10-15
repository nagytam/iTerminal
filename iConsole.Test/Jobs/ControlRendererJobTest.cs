using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Jobs;

namespace iConsole.Jobs.Test;

[TestClass]
public class ControlRendererJobTest
{
    [TestMethod]
    public void Render_WhenDesktopIsNull_ShouldBeIdle()
    {
        var job = new ControlRendererJob();

        // Desktop is null by default; Render should return true (idle)
        var renderMethod = job.GetType().GetMethod("Render", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (bool)renderMethod.Invoke(job, Array.Empty<object>());

        Assert.IsTrue(result);
    }
}

