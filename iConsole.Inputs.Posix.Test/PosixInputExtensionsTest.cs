using iConsole.Inputs;
using iConsole.Inputs.Posix.Extensions;
using iConsole.Inputs.Posix.Jobs;
using iConsole.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Inputs.Posix.Test;

[TestClass]
public class PosixInputExtensionsTest
{
    [TestMethod]
    public void AddLinuxTerminalInput_ReturnsOriginalServiceCollection()
    {
        ServiceCollection services = [];

        IServiceCollection result = services.AddLinuxTerminalInput();

        Assert.AreSame(services, result);
    }

    [TestMethod]
    public void AddLinuxTerminalInput_RegistersLinuxInputServicesOnlyOnLinux()
    {
        ServiceCollection services = [];

        services.AddLinuxTerminalInput();

        if (OperatingSystem.IsLinux())
        {
            Assert.IsTrue(services.Any(service => service.ServiceType == typeof(PosixTerminalInputPublisherJob)));
            Assert.IsTrue(services.Any(service => service.ServiceType == typeof(ITerminalInputSource)));
            Assert.IsTrue(services.Count(service => service.ServiceType == typeof(IJob)) >= 2);
            return;
        }

        Assert.IsFalse(services.Any(service => service.ServiceType == typeof(PosixTerminalInputPublisherJob)));
    }
}
