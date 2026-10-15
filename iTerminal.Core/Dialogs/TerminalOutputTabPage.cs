using iConsole.Interfaces;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>Shows the last terminal command's standard output. See <see cref="TerminalTextTabPage"/>.</summary>
public class TerminalOutputTabPage(
    ITerminalModel terminalModel,
    IThemeService themeService)
    : TerminalTextTabPage("1-Output", ConsoleKey.D1, terminalModel, themeService)
{
    protected override string? SelectText(ITerminalCommand command) => command.Output;

    protected override string EmptyText => "(the command wrote no output)";
}
