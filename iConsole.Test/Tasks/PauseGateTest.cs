using iConsole.Tasks;

namespace iConsole.Tasks.Test;

[TestClass]
public class PauseGateTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task AGate_IsOpenUntilPaused_AndWaitsUntilResumed()
    {
        var gate = new PauseGate();
        Assert.IsFalse(gate.IsPaused);
        await gate.WaitWhilePausedAsync(CancellationToken.None);

        gate.Pause();
        var waiting = gate.WaitWhilePausedAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.IsFalse(waiting.IsCompleted, "a paused gate holds its caller");

        gate.Resume();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(gate.IsPaused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AWaitingCaller_IsCancelledWithItsToken()
    {
        var gate = new PauseGate();
        gate.Pause();
        using var cancellation = new CancellationTokenSource();
        var waiting = gate.WaitWhilePausedAsync(cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => waiting);
    }
}
