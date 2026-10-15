using iConsole.Interfaces;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>Shows the last terminal command's standard error. See <see cref="TerminalTextTabPage"/>.</summary>
public class TerminalErrorTabPage(
    ITerminalModel terminalModel,
    IThemeService themeService)
    : TerminalTextTabPage("2-Error", ConsoleKey.D2, terminalModel, themeService)
{
    protected override string? SelectText(ITerminalCommand command) => command.Error;

    protected override string EmptyText => "(no errors)";
}
