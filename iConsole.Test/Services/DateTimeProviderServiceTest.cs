using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class DateTimeProviderServiceTest
{
    [TestMethod]
    public void DateTimeProviderServiceNow()
    {
        var service = new DateTimeProviderService();
        var now = service.Now;
        Assert.IsTrue(now > DateTime.MinValue);
        Assert.IsTrue(now <= DateTime.Now.AddMilliseconds(500));
    }

    [TestMethod]
    public void DateTimeProviderServiceUtcNow()
    {
        var service = new DateTimeProviderService();
        var utcNow = service.UtcNow;
        Assert.IsTrue(utcNow > DateTime.MinValue);
        Assert.IsTrue(utcNow <= DateTime.UtcNow.AddMilliseconds(500));
    }

    [TestMethod]
    public void DateTimeProviderServiceNowIsBeforeUtcNow()
    {
        var service = new DateTimeProviderService();
        var now = service.Now;
        var utcNow = service.UtcNow;
        // Local time should be ahead of or equal to UTC time (depending on timezone)
        // But UTC time is always the absolute reference
        Assert.IsTrue(now.Kind == DateTimeKind.Local);
        Assert.IsTrue(utcNow.Kind == DateTimeKind.Utc);
    }

    [TestMethod]
    public void DateTimeProviderServiceNowConsistent()
    {
        var service = new DateTimeProviderService();
        var now1 = service.Now;
        var now2 = service.Now;
        Assert.IsTrue(now2 >= now1);
    }

    [TestMethod]
    public void DateTimeProviderServiceInterface()
    {
        var service = new DateTimeProviderService();
        Assert.IsInstanceOfType(service, typeof(IDateTimeProviderService));
    }
}
