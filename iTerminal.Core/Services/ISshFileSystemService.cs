using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services;

public interface ISshFileSystemService : IRemoteFileSystemService
{
    /// <summary>Connects via SSH key auth, password auth, or both (SSH.NET tries the supplied
    /// methods in order). At least one of <paramref name="password"/>/<paramref name="privateKeyFilePath"/>
    /// must be non-empty.</summary>
    void Connect(string host, string username, string? password, string? privateKeyFilePath);
    void Execute(ITerminalCommand command);
}
