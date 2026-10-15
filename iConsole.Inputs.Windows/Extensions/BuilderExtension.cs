using iConsole.Application;
using iConsole.Inputs;
using iConsole.Inputs.Windows.Data;
using iConsole.Inputs.Windows.Jobs;
using iConsole.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;

namespace iConsole.Inputs.Windows.Extensions;

public static class BuilderExtension
{
    public static async Task<IServiceCollection> AddInputWindowsAsync(this IServiceCollection services)
    {

        services.AddSingleton<IConfiguration>(serviceProvider =>
        {
            var configurationRoot = new ConfigurationBuilder()
                .AddJsonFile("appSettings.json")
                .Build();

            return configurationRoot;
        }
        );

        var serviceProvider = services.BuildServiceProvider();

        if (OperatingSystem.IsWindows())
        {
            var featureManager = serviceProvider.GetService<IFeatureManager>();
            if ((featureManager != null)
                && (await featureManager.IsEnabledAsync(nameof(Features.WindowsConsoleDriver))))
            {
                services.AddSingleton<MouseEventPublisherJob>();
                services.AddSingleton<ITerminalInputSource>(sp => sp.GetRequiredService<MouseEventPublisherJob>());
                services.AddSingleton<IJob>(sp => sp.GetRequiredService<MouseEventPublisherJob>());
                services.AddSingleton<IJob, TerminalInputConsumerJob>();
            }
            else
            {
                services.AddPollingConsoleKeyboard();
            }
        }
        else
        {
            services.AddPollingConsoleKeyboard();
        }

        return services;
    }
}
