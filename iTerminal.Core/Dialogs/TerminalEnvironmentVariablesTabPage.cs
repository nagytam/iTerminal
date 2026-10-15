using iConsole.Interfaces;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>Shows the environment the last terminal command ran with. See <see cref="TerminalTextTabPage"/>.</summary>
public class TerminalEnvironmentVariablesTabPage(
    ITerminalModel terminalModel,
    IThemeService themeService)
    : TerminalTextTabPage("3-Environment Variables", ConsoleKey.D3, terminalModel, themeService)
{
    protected override string? SelectText(ITerminalCommand command) => command.EnvironmentVariables;

    protected override string EmptyText => "(no environment variables were recorded)";
}
