using iConsole.Tasks;
using iTerminal.Core.Controls.PopUpWindows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Controls.PopUpWindows.Test;

/// <summary>A progress window's bar sweeps while the task has not reported any progress, and fills once it does.</summary>
[TestClass]
public class ProgressWindowBusyBarTest
{
    private sealed class QuietTask : BackgroundTask
    {
        public override Task ExecuteAsync() => Task.CompletedTask;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ATaskThatHasNotReportedProgressYetShowsTheBusyBar()
    {
        Assert.IsTrue(ProgressWindow.ShowsBusyBar(new QuietTask()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFinishedTaskNeverShowsTheBusyBar()
    {
        var task = new QuietTask();

        await task.RunAsync();

        Assert.IsFalse(ProgressWindow.ShowsBusyBar(task));
    }
}
