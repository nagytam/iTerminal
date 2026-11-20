using System.Net;
using iTerminal.Core.Services.Sharing;

namespace iTerminal.Core.Test.Services.Sharing;

[TestClass]
public class ConnectionLimiterTest
{
    private static readonly IPAddress Host = IPAddress.Parse("192.168.1.30");
    private static readonly IPAddress Other = IPAddress.Parse("192.168.1.31");

    [TestMethod]
    public void TryAcquire_RefusesAHostAboveItsLimit()
    {
        ConnectionLimiter limiter = new(maxPerAddress: 2);

        Assert.IsTrue(limiter.TryAcquire(Host));
        Assert.IsTrue(limiter.TryAcquire(Host));
        Assert.IsFalse(limiter.TryAcquire(Host), "a third connection from one host is refused");
        Assert.IsTrue(limiter.TryAcquire(Other), "another host is not affected");
    }

    [TestMethod]
    public void Release_FreesASlotForTheSameHost()
    {
        ConnectionLimiter limiter = new(maxPerAddress: 1);
        Assert.IsTrue(limiter.TryAcquire(Host));

        limiter.Release(Host);

        Assert.IsTrue(limiter.TryAcquire(Host));
        Assert.AreEqual(1, limiter.OpenFrom(Host));
    }

    [TestMethod]
    public void TryAcquire_WithoutAnAddress_IsNotLimited()
    {
        ConnectionLimiter limiter = new(maxPerAddress: 1);

        Assert.IsTrue(limiter.TryAcquire(null));
        Assert.IsTrue(limiter.TryAcquire(null));
    }
}
