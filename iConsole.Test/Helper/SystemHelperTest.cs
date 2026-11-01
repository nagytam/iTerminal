using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class SystemHelperTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void SystemHelperGetAvailableMemory()
    {
        var memory = SystemHelper.GetAvailableMemoryInMBs();
        Assert.IsTrue(memory > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SystemHelperMemoryLessThanMax()
    {
        var memory = SystemHelper.GetAvailableMemoryInMBs();
        // Assuming system has less than 2TB of RAM
        Assert.IsTrue(memory < 2000000);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SystemHelperMemoryIsLong()
    {
        var memory = SystemHelper.GetAvailableMemoryInMBs();
        Assert.IsInstanceOfType(memory, typeof(long));
    }
}
