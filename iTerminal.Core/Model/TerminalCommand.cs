using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Model;

public class TerminalCommand : ITerminalCommand
{
    public Url WorkingDirectoryUrl { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public int ExitCode { get; set; } = 0;
    public DateTime StartTime { get; set; } = DateTime.MinValue;
    public DateTime ExitTime { get; set; } = DateTime.MinValue;
    public string? Output { get; set; } = null;
    public string? Error { get; set; } = null;
    public string? EnvironmentVariables { get; set; } = null;
    public Task? CommandTask { get; set; } = null;
    public string Host { get; set; }
    public string Username { get; set; }
}
