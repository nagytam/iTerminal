using iConsole.Services;
using iTerminal.Core.Services.Taskbar;

namespace iTerminal.Core.Test.Services.Taskbar;

[TestClass]
public class TerminalTaskbarProgressTest
{
    private sealed class RecordingTerminalProgress : ITerminalProgressService
    {
        public List<(TerminalProgressState State, long Percentage)> Updates { get; } = [];

        public void Update(TerminalProgressState state, long percentage) => Updates.Add((state, percentage));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Map_TaskbarStatesBecomeTheTerminalStates()
    {
        Assert.AreEqual(TerminalProgressState.Remove, TerminalTaskbarProgress.Map(TaskbarState.None));
        Assert.AreEqual(TerminalProgressState.Indeterminate, TerminalTaskbarProgress.Map(TaskbarState.Indeterminate));
        Assert.AreEqual(TerminalProgressState.Normal, TerminalTaskbarProgress.Map(TaskbarState.Normal));
        Assert.AreEqual(TerminalProgressState.Paused, TerminalTaskbarProgress.Map(TaskbarState.Paused));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Update_ForwardsTheMappedStateAndThePercentage()
    {
        var progress = new RecordingTerminalProgress();
        var taskbar = new TerminalTaskbarProgress(progress);

        taskbar.Update(TaskbarState.Normal, 45);

        CollectionAssert.AreEqual(new[] { (TerminalProgressState.Normal, 45L) }, progress.Updates);
    }
}
