using iTerminal.Core.Services.Nfs;

namespace iTerminal.Core.Test.Services.Nfs;

/// <summary>
/// One mount per export is kept for the run; a failed operation evicts it so the next one mounts again, and a mount
/// that cannot be made is reported with the host's name.
/// </summary>
[TestClass]
public class NfsMountCacheTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task ASuccessfulExportIsMountedOnceAndReused()
    {
        var factory = new FakeNfsMountFactory();
        await using var cache = new NfsMountCache(factory);

        await cache.ExecuteAsync("nas", "/srv", m => m.CreateFolderAsync("a", CancellationToken.None), CancellationToken.None);
        await cache.ExecuteAsync("nas", "/srv", m => m.CreateFolderAsync("b", CancellationToken.None), CancellationToken.None);

        Assert.AreEqual(1, factory.MountCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedOperationEvictsTheMountAndTheNextCallMountsAgain()
    {
        var factory = new FakeNfsMountFactory();
        await using var cache = new NfsMountCache(factory);
        await cache.ExecuteAsync("nas", "/srv", m => m.CreateFolderAsync("a", CancellationToken.None), CancellationToken.None);
        var dead = factory.On("nas", "/srv");
        dead.Fail = new TimeoutException("no reply");

        var failure = await Assert.ThrowsExactlyAsync<IOException>(() =>
            cache.ExecuteAsync("nas", "/srv", m => m.CreateFolderAsync("b", CancellationToken.None), CancellationToken.None));
        // The server is back: the next call is a fresh connection, not the dead one.
        factory.Mounts.Remove("nas|/srv");
        await cache.ExecuteAsync("nas", "/srv", m => m.CreateFolderAsync("c", CancellationToken.None), CancellationToken.None);

        StringAssert.Contains(failure.Message, "nas");
        Assert.IsTrue(dead.Disposed, "the dead connection is disposed");
        Assert.AreEqual(2, factory.MountCount, "the next operation mounts again");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMountThatCannotBeMadeNamesTheHost()
    {
        var factory = new FakeNfsMountFactory { MountFailure = new IOException("connection refused") };
        await using var cache = new NfsMountCache(factory);

        var failure = await Assert.ThrowsExactlyAsync<IOException>(() =>
            cache.ExecuteAsync("nas.example", "/srv", m => m.CreateFolderAsync("a", CancellationToken.None), CancellationToken.None));

        StringAssert.Contains(failure.Message, "nas.example");
        StringAssert.Contains(failure.Message, "connection refused");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CancellationIsNotWrappedAsAConnectionFailure()
    {
        var factory = new FakeNfsMountFactory();
        await using var cache = new NfsMountCache(factory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            await cache.ExecuteAsync("nas", "/srv", m => m.CreateFolderAsync("a", cancellation.Token), cancellation.Token);
            Assert.Fail("a cancelled call must not succeed");
        }
        catch (OperationCanceledException)
        {
            // Expected: cancellation passes through unchanged, not as an IOException.
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AServerRefusalKeepsTheSharedMountForTheOtherPanel()
    {
        var factory = new FakeNfsMountFactory();
        await using var cache = new NfsMountCache(factory);
        await cache.ExecuteAsync("nas", "/srv", m => m.CreateFolderAsync("a", CancellationToken.None), CancellationToken.None);
        var shared = factory.On("nas", "/srv");
        shared.Fail = new NfsSharp.Protocol.NfsException("permission denied");

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            cache.ExecuteAsync("nas", "/srv", m => m.DeleteFileAsync("locked", CancellationToken.None), CancellationToken.None));
        shared.Fail = null;
        await cache.ExecuteAsync("nas", "/srv", m => m.CreateFolderAsync("b", CancellationToken.None), CancellationToken.None);

        Assert.IsFalse(shared.Disposed, "the server answered, so the other panel's mount is not torn down");
        Assert.AreEqual(1, factory.MountCount);
    }
}
