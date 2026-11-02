using iConsole.Inputs.Windows.Extensions;
using iConsole.Inputs.Windows.Jobs;
using iConsole.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Inputs.Windows.Test;

[TestClass]
public class BuilderExtensionCoverageTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task AddInputWindowsAsync_RegistersFallbackKeyboardJobsWhenFeatureFlagIsMissing()
    {
        ServiceCollection services = [];

        IServiceCollection result = await services.AddInputWindowsAsync();

        Assert.AreSame(services, result);
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(IJob) && service.ImplementationType == typeof(KeyboardEventPublisherJob)));
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(IJob) && service.ImplementationType == typeof(KeyboardEventConsumerJob)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AddInputWindowsAsync_UsesTheFinalConfigurationSourceForTheFeatureFlag()
    {
        const string featureKey = "FeatureManagement:WindowsConsoleDriver";
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [featureKey] = "false" })
            .AddInMemoryCollection(new Dictionary<string, string?> { [featureKey] = "true" })
            .Build();
        ServiceCollection services = [];

        await services.AddInputWindowsAsync(configuration);

        if (OperatingSystem.IsWindows())
        {
            Assert.IsTrue(services.Any(service => service.ServiceType == typeof(IJob)
                && service.ImplementationType == typeof(TerminalInputConsumerJob)));
            Assert.IsTrue(services.Any(service => service.ServiceType == typeof(MouseEventPublisherJob)));
        }
        else
        {
            Assert.IsTrue(services.Any(service => service.ServiceType == typeof(IJob)
                && service.ImplementationType == typeof(KeyboardEventPublisherJob)));
        }
    }
}
