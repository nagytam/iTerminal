using iConsole.Services;
using Moq;

namespace iConsole.Test.Services;

[TestClass]
public class FrequentExecutionGuardServiceTest
{
    [TestMethod]
    public void FrequentExecutionGuardServiceExecuteImmediate()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        mockDateTimeProvider.Setup(m => m.Now).Returns(DateTime.Now);

        var service = new FrequentExecutionGuardService(mockDateTimeProvider.Object);
        int executionCount = 0;

        service.Execute(() => executionCount++);

        Assert.AreEqual(1, executionCount);
    }

    [TestMethod]
    public void FrequentExecutionGuardServiceExecuteMultiple()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        var now = DateTime.Now;
        mockDateTimeProvider.Setup(m => m.Now)
            .Returns(() => now)
            .Callback(() => now = now.AddSeconds(1)); // Move time forward after each call

        var service = new FrequentExecutionGuardService(mockDateTimeProvider.Object);
        int executionCount = 0;

        service.Execute(() => executionCount++);
        service.Execute(() => executionCount++);

        // First execution happens, second is delayed or skipped
        Assert.IsTrue(executionCount > 0);
    }

    [TestMethod]
    public void FrequentExecutionGuardServiceThrottle()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        var now = DateTime.Now;
        mockDateTimeProvider.Setup(m => m.Now).Returns(() => now);

        var service = new FrequentExecutionGuardService(mockDateTimeProvider.Object);
        int executionCount = 0;

        service.Execute(() => executionCount++);
        service.Execute(() => executionCount++); // Called immediately, should be throttled

        Assert.AreEqual(1, executionCount);
    }

    [TestMethod]
    public void FrequentExecutionGuardServiceInterface()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        mockDateTimeProvider.Setup(m => m.Now).Returns(DateTime.Now);

        var service = new FrequentExecutionGuardService(mockDateTimeProvider.Object);
        Assert.IsInstanceOfType(service, typeof(IFrequentExecutionGuardService));
    }

    [TestMethod]
    public void FrequentExecutionGuardServiceWithActionException()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        mockDateTimeProvider.Setup(m => m.Now).Returns(DateTime.Now);

        var service = new FrequentExecutionGuardService(mockDateTimeProvider.Object);

        try
        {
            service.Execute(() => throw new InvalidOperationException());
            Assert.Fail("Should have thrown exception");
        }
        catch (InvalidOperationException)
        {
            // Expected
        }
    }
}
