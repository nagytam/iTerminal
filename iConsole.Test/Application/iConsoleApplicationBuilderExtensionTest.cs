using iConsole.Application;
using iConsole.Drivers;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Test.Application;

[TestClass]
public class iConsoleApplicationBuilderExtensionTest
{
    [TestMethod]
    public void AddDefaultConsoleDriver_RegistersExpectedDriverForCurrentPlatform()
    {
        var services = new ServiceCollection();

        services.AddDefaultConsoleDriver();

        var registration = services.Single(s => s.ServiceType == typeof(IConsoleDriver));

        var expectedImplementationType = OperatingSystem.IsWindows()
            ? typeof(SystemConsoleDriver)
            : typeof(VirtualSequenceConsoleDriver);

        Assert.AreEqual(expectedImplementationType, registration.ImplementationType);
        Assert.AreEqual(ServiceLifetime.Singleton, registration.Lifetime);
    }

    [TestMethod]
    public void AddDefaultConsoleDriver_ReturnsOriginalServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddDefaultConsoleDriver();

        Assert.AreSame(services, result);
    }
}