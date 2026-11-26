using iConsole.Dialogs;

namespace iTerminal.Core.Extensions;

/// <summary>
/// Creates the dialog that opens a <c>.sln</c> or <c>.slnx</c> (F3 on a solution). The Development Studio provides it;
/// iTerminal.Core does not reference that project, so it asks for the dialog through this contract.
/// </summary>
public interface ISolutionDialogFactory
{
    /// <summary>A new dialog, opened on the solution at <paramref name="solutionPath"/>.</summary>
    Dialog Create(string solutionPath);
}
