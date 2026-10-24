using iConsole.Tasks;

namespace iConsole.Tasks.Test;

[TestClass]
public class GatedStreamTest
{
    [TestMethod]
    public async Task AWrite_WaitsWhileTheGateIsPaused_AndLandsOnceResumed()
    {
        var gate = new PauseGate();
        var inner = new MemoryStream();
        var gated = new GatedStream(inner, gate);
        gate.Pause();

        var write = gated.WriteAsync(new byte[] { 1, 2, 3 }, 0, 3, CancellationToken.None);
        await Task.Delay(50);
        Assert.IsFalse(write.IsCompleted);
        Assert.AreEqual(0, inner.Length, "nothing is written while paused");

        gate.Resume();
        await write.WaitAsync(TimeSpan.FromSeconds(5));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, inner.ToArray());
    }

    [TestMethod]
    public void ItCannotBeReadOrSought()
    {
        var gated = new GatedStream(new MemoryStream(), new PauseGate());

        Assert.IsFalse(gated.CanRead);
        Assert.IsFalse(gated.CanSeek);
        Assert.IsTrue(gated.CanWrite);
        Assert.ThrowsExactly<NotSupportedException>(() => gated.Read(new byte[1], 0, 1));
    }
}
