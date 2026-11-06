using iConsole.Application;
using iConsole.Inputs;
using iConsole.Inputs.Windows.Data;
using iConsole.Inputs.Windows.Jobs;
using iConsole.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Inputs.Windows.Extensions;

public static class BuilderExtension
{
    public static Task<IServiceCollection> AddInputWindowsAsync(this IServiceCollection services)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appSettings.json", optional: true)
            .Build();

        return services.AddInputWindowsAsync(configuration);
    }

    public static Task<IServiceCollection> AddInputWindowsAsync(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration);

        bool useWindowsConsoleDriver = configuration.GetValue<bool>(
            $"FeatureManagement:{nameof(Features.WindowsConsoleDriver)}");
        if (OperatingSystem.IsWindows() && useWindowsConsoleDriver)
        {
            services.AddSingleton<MouseEventPublisherJob>();
            services.AddSingleton<ITerminalInputSource>(serviceProvider => serviceProvider.GetRequiredService<MouseEventPublisherJob>());
            services.AddSingleton<IJob>(serviceProvider => serviceProvider.GetRequiredService<MouseEventPublisherJob>());
            services.AddSingleton<IJob, TerminalInputConsumerJob>();
        }
        else
        {
            services.AddPollingConsoleKeyboard();
        }

        return Task.FromResult(services);
    }
}
