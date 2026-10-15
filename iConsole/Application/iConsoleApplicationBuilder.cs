using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace iConsole.Application;

public class iConsoleApplicationBuilder
{
    private readonly ServiceCollection _services = new();
    private readonly HostBuilder _hostBuilder = new();
    public IServiceCollection Services => _services;

    public IConfiguration Configuration { get; } = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appSettings.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();

    public iConsoleApplication Build()
    {
        _hostBuilder.ConfigureServices((context, services) =>
        {
            foreach (var service in _services)
            {
                services.Add(service);
            }
        });

        var host = _hostBuilder.Build();
        var application = new iConsoleApplication(host);

        return application;
    }
}
