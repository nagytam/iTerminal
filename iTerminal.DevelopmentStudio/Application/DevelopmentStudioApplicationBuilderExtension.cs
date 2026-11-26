using iTerminal.Core.Extensions;
using iTerminal.DevelopmentStudio.Commands;
using iTerminal.DevelopmentStudio.Contributions;
using iTerminal.DevelopmentStudio.Dialogs;
using iTerminal.DevelopmentStudio.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace iTerminal.DevelopmentStudio.Application;

/// <summary>
/// Registers the Development Studio: its scanner, process runner, toolchains and breakpoints, the dialog, the Tools menu
/// entry and the solution dialog that F3 on a <c>.sln</c> or <c>.slnx</c> opens. The composition root calls this after
/// <c>AddiTerminal</c>, as it calls <c>AddWidgets</c>; iTerminal.Core cannot call it, since it does not reference this project.
/// </summary>
public static class DevelopmentStudioApplicationBuilderExtension
{
    public static IServiceCollection AddDevelopmentStudio(this IServiceCollection services)
    {
        services.AddSingleton<IDevelopmentWorkspaceScanner, DevelopmentWorkspaceScanner>();
        services.AddSingleton<IDevelopmentProcessRunner, DevelopmentProcessRunner>();
        services.AddSingleton(_ => new DevelopmentToolchains());
        // The breakpoints are a singleton, so they survive closing the dialog.
        services.AddSingleton<BreakpointStore>();
        services.AddTransient<DevelopmentStudioDialog>();

        // Tools > Development studio, in the "sys" submenu.
        services.AddSingleton<IToolsMenuContribution>(provider =>
            new DevelopmentStudioToolsMenuEntry("sys", new DevelopmentStudioDialogCommand(provider.GetRequiredService<DevelopmentStudioDialog>())));

        // F3 on a solution opens it in the studio, scoped to that solution.
        services.TryAddSingleton<ISolutionDialogFactory, DevelopmentStudioSolutionDialogFactory>();
        return services;
    }
}
