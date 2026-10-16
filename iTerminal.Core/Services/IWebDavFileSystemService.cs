namespace iTerminal.Core.Services;

/// <summary>The <c>dav://</c> and <c>davs://</c> drives' back-end.</summary>
public interface IWebDavFileSystemService : IRemoteFileSystemService, IDisposable
{
    /// <summary>
    /// Points the service at <paramref name="host"/> (<c>host</c> or <c>host:port</c>), over HTTPS when
    /// <paramref name="secure"/>. A null or empty <paramref name="username"/> makes anonymous requests.
    /// Nothing is sent: WebDAV has no session, so the first request is the first conversation.
    /// </summary>
    void Connect(string host, bool secure, string? username, string? password);

    void Disconnect();
}
