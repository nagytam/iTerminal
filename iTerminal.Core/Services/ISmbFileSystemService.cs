using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services;

public interface ISmbFileSystemService : IRemoteFileSystemService
{
    /// <summary>Lists the shares available on <paramref name="host"/>. Best-effort: returns an
    /// empty list rather than throwing if the host is unreachable or the credentials are rejected.</summary>
    IEnumerable<string> ListShares(string host, string username, string password);

    void Connect(string host, string share, string username, string password);
    void Disconnect();
}
