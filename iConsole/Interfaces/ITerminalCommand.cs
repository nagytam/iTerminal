using iConsole.Data;

namespace iConsole.Interfaces;

public interface ITerminalCommand
{
    Url WorkingDirectoryUrl { get; set; }
    DateTime StartTime { get; set; }
    DateTime ExitTime { get; set; }
    int ExitCode { get; set; }
    string? Output { get; set; }
    string? Error { get; set; }
    public string? EnvironmentVariables { get; set; }
    public string Command { get; set; }
    public Task? CommandTask { get; set; }
    string Host { get; set; }
    string Username { get; set; }
}