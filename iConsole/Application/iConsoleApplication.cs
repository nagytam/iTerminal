using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace iConsole.Application;

public class iConsoleApplication(IHost host)
{
    private readonly IHost _host = host;

    public IServiceProvider Services => _host.Services;

    public static iConsoleApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = new iConsoleApplicationBuilder();
        return builder;
    }

    public async Task RunAsync()
    {
        // WaitForShutdownAsync stops the host itself once shutdown is requested, so stopping it again
        // afterwards ran every hosted service's StopAsync twice. The finally only covers the paths
        // that never reached that orderly stop: a failed start or a failed wait.
        var stopped = false;
        try
        {
            await _host.StartAsync();
            await _host.WaitForShutdownAsync();
            stopped = true;
        }
        finally
        {
            if (!stopped)
            {
                try
                {
                    await _host.StopAsync();
                }
                catch
                {
                    // Ignore errors raised while stopping the host during shutdown.
                }
            }
        }
    }
}
