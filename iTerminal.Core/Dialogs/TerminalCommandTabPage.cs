using iConsole.Localization;
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
        string.Format(UiText.Get("Terminal.Tab.Command", "Command: {0}"), command.Command),
        string.Format(UiText.Get("Terminal.Tab.ExitCode", "Exit code: {0}"), command.ExitCode),
        string.Format(UiText.Get("Terminal.Tab.WorkDir", "Working directory: {0}"), command.WorkingDirectoryUrl?.Path),
        string.Format(UiText.Get("Terminal.Tab.Start", "Start time: {0}"), command.StartTime.ToString("yyyy-MM-dd HH:mm:ss")),
        string.Format(UiText.Get("Terminal.Tab.End", "Exit time: {0}"), command.ExitTime.ToString("yyyy-MM-dd HH:mm:ss")),
        string.Format(UiText.Get("Terminal.Tab.Elapsed", "Elapsed time: {0} [msec]"), (command.ExitTime - command.StartTime).TotalMilliseconds),
        string.Format(UiText.Get("Terminal.Tab.Host", "Host: {0}"), command.Host),
        string.Format(UiText.Get("Terminal.Tab.User", "Username: {0}"), command.Username));

    protected override string EmptyText => string.Empty;
}
