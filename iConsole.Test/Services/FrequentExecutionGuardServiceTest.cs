using iConsole.Services;
using Moq;

namespace iConsole.Test.Services;

[TestClass]
public class FrequentExecutionGuardServiceTest
{
    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public void FrequentExecutionGuardServiceInterface()
    {
        var mockDateTimeProvider = new Mock<IDateTimeProviderService>();
        mockDateTimeProvider.Setup(m => m.Now).Returns(DateTime.Now);

        var service = new FrequentExecutionGuardService(mockDateTimeProvider.Object);
        Assert.IsInstanceOfType(service, typeof(IFrequentExecutionGuardService));
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnActionThatThrowsWhileDelayed_DoesNotStopLaterDelayedActions()
    {
        var service = new FrequentExecutionGuardService(new DateTimeProviderService());
        service.Execute(() => { });                                           // immediate: opens the window
        service.Execute(() => throw new InvalidOperationException("boom"));  // delayed, and it fails on its worker
        await Task.Delay(300);

        var ran = false;
        service.Execute(() => ran = true);                                    // must be scheduled again, not dropped

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ran && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.IsTrue(ran, "a delayed action that failed stopped every later one");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheNewestDelayedRequest_IsTheOneThatRuns()
    {
        var service = new FrequentExecutionGuardService(new DateTimeProviderService());
        var ran = new List<string>();
        service.Execute(() => ran.Add("immediate"));
        service.Execute(() => ran.Add("first waiting"));
        service.Execute(() => ran.Add("latest"));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ran.Contains("latest") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        CollectionAssert.AreEqual(new[] { "immediate", "latest" }, ran, "the latest update was dropped");
    }
}
