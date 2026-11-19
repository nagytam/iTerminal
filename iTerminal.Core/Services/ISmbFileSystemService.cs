using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services;

public interface ISmbFileSystemService : IRemoteFileSystemService
{
    /// <summary>Lists the shares available on <paramref name="host"/>. Best-effort: returns an
    /// empty list rather than throwing if the host is unreachable or the credentials are rejected.</summary>
    IEnumerable<string> ListShares(string host, string username, string password);

    /// <summary>Lists the shares on <paramref name="host"/> as <paramref name="username"/>. Null when the host cannot be
    /// reached or the login is refused, so a caller can tell a wrong password from a host with no shares.</summary>
    IReadOnlyList<string>? TryListShares(string host, string username, string password);

    void Connect(string host, string share, string username, string password);
    void Disconnect();
}
