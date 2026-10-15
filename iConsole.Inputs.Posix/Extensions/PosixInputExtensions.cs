using iConsole.Inputs;
using iConsole.Inputs.Posix.Jobs;
using iConsole.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace iConsole.Inputs.Posix.Extensions;

public static class PosixInputExtensions
{
    /// <summary>
    /// Linux: ANSI/SGR mouse + raw stdin via <see cref="PosixTerminalInputPublisherJob"/> and <see cref="TerminalInputConsumerJob"/>.
    /// Call after <c>AddiConsoleCore</c> from <c>iConsole.Application</c>.
    /// </summary>
    public static IServiceCollection AddLinuxTerminalInput(this IServiceCollection services)
    {
        if (!OperatingSystem.IsLinux())
        {
            return services;
        }

        services.AddSingleton<PosixTerminalInputPublisherJob>();
        services.AddSingleton<ITerminalInputSource>(sp => sp.GetRequiredService<PosixTerminalInputPublisherJob>());
        services.AddSingleton<IJob>(sp => sp.GetRequiredService<PosixTerminalInputPublisherJob>());
        services.AddSingleton<IJob, TerminalInputConsumerJob>();
        return services;
    }
}
