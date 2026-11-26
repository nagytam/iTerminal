using iConsole.Commands;
using iConsole.Dialogs;
using iTerminal.Core.Extensions;
using iTerminal.DevelopmentStudio.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.DevelopmentStudio.Contributions;

/// <summary>A Tools menu entry the studio adds to the menu of iTerminal.Core.</summary>
public sealed record DevelopmentStudioToolsMenuEntry(string Group, ICommand Command) : IToolsMenuContribution;

/// <summary>Opens a solution in a fresh studio dialog, scoped to that solution (F3 on a <c>.sln</c> or <c>.slnx</c>).</summary>
public sealed class DevelopmentStudioSolutionDialogFactory(IServiceProvider services) : ISolutionDialogFactory
{
    public Dialog Create(string solutionPath)
    {
        var dialog = services.GetRequiredService<DevelopmentStudioDialog>();
        dialog.SolutionPath = solutionPath;
        return dialog;
    }
}
