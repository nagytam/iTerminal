using iConsole.Interfaces;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>What the last terminal command was and how it ended. See <see cref="TerminalTextTabPage"/>.</summary>
public class TerminalCommandTabPage(
    ITerminalModel terminalModel,
    IThemeService themeService)
    : TerminalTextTabPage("4-Command", ConsoleKey.D4, terminalModel, themeService)
{
    protected override string? SelectText(ITerminalCommand command) => string.Join('\n',
        $"Command: {command.Command}",
        $"Exit code: {command.ExitCode}",
        $"Working directory: {command.WorkingDirectoryUrl?.Path}",
        $"Start time: {command.StartTime:yyyy-MM-dd HH:mm:ss}",
        $"Exit time: {command.ExitTime:yyyy-MM-dd HH:mm:ss}",
        $"Elapsed time: {(command.ExitTime - command.StartTime).TotalMilliseconds} [msec]",
        $"Host: {command.Host}",
        $"Username: {command.Username}");

    protected override string EmptyText => string.Empty;
}
