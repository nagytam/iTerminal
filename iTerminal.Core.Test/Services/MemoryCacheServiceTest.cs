using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Test;

/// <summary>The short-lived cache: a value comes back by its key, a missing key reads as null, and an entry leaves when its time is up.</summary>
[TestClass]
public class MemoryCacheServiceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void SetThenGet_ReturnsTheValue()
    {
        var cache = new MemoryCacheService();

        cache.Set("folder", "contents");

        Assert.AreEqual("contents", cache.Get("folder"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Get_OfAMissingKey_IsNull()
    {
        Assert.IsNull(new MemoryCacheService().Get("nothing"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetOverAnExistingKey_ReplacesTheValue()
    {
        var cache = new MemoryCacheService();
        cache.Set("k", "first");

        cache.Set("k", "second");

        Assert.AreEqual("second", cache.Get("k"));
    }

    [TestMethod]
    [Timeout(20_000)]
    public async Task AnEntryWithAShortLifetime_LeavesTheCacheWhenItsTimeIsUp()
    {
        var cache = new MemoryCacheService();
        cache.Set("brief", "value", TimeSpan.FromMilliseconds(50));

        // A deadline, not a fixed wait: the expiry runs on a timer that a loaded machine may run late.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (cache.Get("brief") is not null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.IsNull(cache.Get("brief"));
    }
}
