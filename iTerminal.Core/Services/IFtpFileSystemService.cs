namespace iTerminal.Core.Services;

/// <summary>The <c>ftp://</c> drive's back-end.</summary>
public interface IFtpFileSystemService : IRemoteFileSystemService, IDisposable
{
    /// <summary>
    /// Connects to <paramref name="host"/> (<c>host</c> or <c>host:port</c>). A null or empty
    /// <paramref name="username"/> logs in as <c>anonymous</c>.
    /// </summary>
    void Connect(string host, string? username, string? password);

    void Disconnect();
}
