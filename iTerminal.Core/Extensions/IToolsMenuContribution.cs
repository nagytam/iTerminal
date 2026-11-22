using iConsole.Commands;

namespace iTerminal.Core.Extensions;

/// <summary>
/// A Tools menu entry that a feature outside iTerminal.Core adds (the online information widgets, for one). The
/// menu places it in the submenu named by <see cref="Group"/>: "files", "net", "office", "games" or "sys".
/// </summary>
public interface IToolsMenuContribution
{
    string Group { get; }

    ICommand Command { get; }
}
