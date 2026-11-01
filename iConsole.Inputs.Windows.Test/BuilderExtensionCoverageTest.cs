using iConsole.Inputs.Windows.Extensions;
using iConsole.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Inputs.Windows.Test;

[TestClass]
public class BuilderExtensionCoverageTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task AddInputWindowsAsync_RegistersFallbackKeyboardJobsWhenFeatureManagerIsUnavailable()
    {
        ServiceCollection services = [];

        IServiceCollection result = await services.AddInputWindowsAsync();

        Assert.AreSame(services, result);
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(IJob) && service.ImplementationType == typeof(KeyboardEventPublisherJob)));
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(IJob) && service.ImplementationType == typeof(KeyboardEventConsumerJob)));
    }
}
