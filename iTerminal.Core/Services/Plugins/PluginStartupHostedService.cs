using iConsole.Localization;
using iConsole.Services;
using Microsoft.Extensions.Hosting;

namespace iTerminal.Core.Services.Plugins;

/// <summary>
/// Loads the plug-ins the user loaded last time, once the database (and so the settings) is ready. If some of them
/// fail, a notification says so, and the plug-in manager says why.
/// </summary>
public sealed class PluginStartupHostedService(IPluginManagerService plugins, INotificationService? notifications = null) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        int failed = await plugins.StartAsync();
        if (failed > 0)
        {
            notifications?.Notify(
                UiText.Get("Plugins.Title", "Plugin manager"),
                string.Format(UiText.Get("Plugins.Notify.Failed", "{0} plug-in(s) could not be loaded. The plug-in manager in the Tools menu says why."), failed));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
