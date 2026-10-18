using iConsole.Interfaces;
using iTerminal.Core.Application;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace iTerminal.Core.Test.Application;

[TestClass]
public class iTerminalApplicationBuilderExtensionTest
{
    [TestMethod]
    public void AddiTerminal_ReturnsOriginalServiceCollectionAndRegistersCoreServices()
    {
        ServiceCollection services = [];

        IServiceCollection result = services.AddiTerminal();

        Assert.AreSame(services, result);
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(IHostedService) && service.ImplementationType == typeof(DatabaseInitializationHostedService)));
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(IContentModelService) && service.ImplementationType == typeof(ContentModelService)));
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(ISettingsService) && service.ImplementationType == typeof(SettingsService)));
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(IMemoryCacheService) && service.ImplementationType == typeof(MemoryCacheService)));
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(Serilog.ILogger) && service.ImplementationFactory is not null));
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(ContentBrowser)));
        Assert.IsTrue(services.Any(service => service.ServiceType == typeof(IDbContextFactory<iTerminalDbContext>)));
    }
}
