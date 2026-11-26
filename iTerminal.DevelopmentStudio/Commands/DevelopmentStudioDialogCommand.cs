using iTerminal.DevelopmentStudio.Dialogs;
using iTerminal.Core.Commands;

namespace iTerminal.DevelopmentStudio.Commands;

public class DevelopmentStudioDialogCommand(DevelopmentStudioDialog developmentStudioDialog) : ShowDialogCommand<DevelopmentStudioDialog>(developmentStudioDialog)
{
    public override string Title => "Development studio";
    public override string Description => "Solution Explorer and Test Explorer for .NET solutions, Python and PowerShell; build, run, clean and test; a debugger with breakpoints, a watch and the call stack.";
}
