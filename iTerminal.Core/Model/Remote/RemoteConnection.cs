using iConsole.Data;
using System.Net;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Model.Remote;

/// <summary>What a saved <see cref="RemoteConnection"/> speaks.</summary>
public enum RemoteProtocol
{
    Ssh,
    PowerShell,

    /// <summary>A file drive, <c>ftp://</c> (FTPS is negotiated by the service). Opened as a panel, never as a shell.</summary>
    Ftp,

    /// <summary>A file drive, <c>dav://</c>, or <c>davs://</c> when <see cref="RemoteConnection.Secure"/> is set.</summary>
    WebDav,
}

/// <summary>How a PowerShell remoting session reaches its host.</summary>
public enum PowerShellTransport
{
    /// <summary>WS-Management (<c>Enter-PSSession -ComputerName</c>): Windows hosts, Windows clients only.</summary>
    WinRm,

    /// <summary>PowerShell over SSH (<c>Enter-PSSession -HostName</c>): any host with an SSH server and PowerShell 7 on this side.</summary>
    Ssh,
}

/// <summary>
/// One entry of the remote connection manager: where to connect and how. It never holds a password —
/// <c>ssh</c> and PowerShell ask for it on the terminal they are given, which is also where a key's
/// passphrase or a host-key question belongs. An FTP or WebDAV entry has no terminal to ask on, so its
/// password lives in the password manager under <see cref="Id"/>, the way the drives look it up
/// (<see cref="HasSavedCredential"/> only says that one is there).
/// </summary>
public sealed partial class RemoteConnection
{
    public const int MaxNameLength = 60;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public RemoteProtocol Protocol { get; set; } = RemoteProtocol.Ssh;
    public string Host { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>Private key to authenticate with (SSH, and PowerShell over SSH); empty for the ssh defaults — agent, config, default keys.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>PowerShell only.</summary>
    public PowerShellTransport Transport { get; set; } = PowerShellTransport.WinRm;

    public string? Notes { get; set; }
    public DateTime? LastConnectedUtc { get; set; }

    /// <summary>WebDAV only: HTTPS (<c>davs://</c>) rather than HTTP.</summary>
    public bool Secure { get; set; }

    /// <summary>FTP and WebDAV: the folder the panel opens in; empty is the root.</summary>
    public string? RemotePath { get; set; }

    /// <summary>FTP and WebDAV: a password for this entry is in the password manager.</summary>
    public bool HasSavedCredential { get; set; }

    /// <summary>The port the connection actually uses.</summary>
    public int EffectivePort => Port ?? DefaultPort;

    public int DefaultPort => Protocol switch
    {
        RemoteProtocol.Ftp => 21,
        RemoteProtocol.WebDav => Secure ? 443 : 80,
        RemoteProtocol.PowerShell when Transport == PowerShellTransport.WinRm => 5985,
        _ => 22,
    };

    public bool UsesSsh => Protocol == RemoteProtocol.Ssh || (Protocol == RemoteProtocol.PowerShell && Transport == PowerShellTransport.Ssh);

    /// <summary>FTP and WebDAV: opened as a panel drive; there is no shell to lend the terminal to.</summary>
    public bool IsFileDrive => Protocol is RemoteProtocol.Ftp or RemoteProtocol.WebDav;

    /// <summary>
    /// The host as the drives see it — <c>host</c> or <c>host:port</c> — which is what a saved
    /// <see cref="Network.NetworkConnectionProfile"/> is matched on. The default port is left off, because
    /// a URL without one carries the bare host.
    /// </summary>
    public string DriveHost => Port is { } port && port != DefaultPort ? $"{Host}:{port}" : Host;

    /// <summary>The drive URL of a file-drive entry: <c>ftp://user@host[:port]/path</c> or <c>dav[s]://…</c>.</summary>
    public string DriveUrl
    {
        get
        {
            var scheme = Protocol switch
            {
                RemoteProtocol.Ftp => Container.FTP,
                RemoteProtocol.WebDav when Secure => Container.WEBDAV_SECURE,
                _ => Container.WEBDAV,
            };
            var path = (RemotePath ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
            var user = string.IsNullOrEmpty(Username) ? string.Empty : Username + "@";
            return $"{scheme}{user}{DriveHost}/{path}";
        }
    }

    /// <summary><c>user@host</c>, or just the host when no user is set.</summary>
    public string Destination => string.IsNullOrEmpty(Username) ? Host : $"{Username}@{Host}";

    /// <summary>Why this entry cannot be saved or connected to, or null when it is sound.</summary>
    public string? Validate()
    {
        var name = Name.Trim();
        if (name.Length == 0)
        {
            return "Give the connection a name, so it can be found in the list.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"The name is longer than {MaxNameLength} characters.";
        }

        if (!IsValidHost(Host))
        {
            return "The host must be a host name or an IP address.";
        }

        if (Port is { } port && (port < 1 || port > 65535))
        {
            return "The port must be between 1 and 65535.";
        }

        if (!IsValidUsername(Username))
        {
            return "The user name has characters that cannot be passed on safely.";
        }

        if (!string.IsNullOrEmpty(PrivateKeyPath) && PrivateKeyPath.Any(char.IsControl))
        {
            return "The key file path has control characters.";
        }

        if (IsFileDrive)
        {
            // The user name is written into a URL, where these end it early.
            if (Username.IndexOfAny(['@', '/', '\\', ':']) >= 0)
            {
                return "An FTP or WebDAV user name cannot contain @ / \\ or : (it is written into the drive's address).";
            }

            if (!string.IsNullOrEmpty(RemotePath) && RemotePath.Any(char.IsControl))
            {
                return "The folder has control characters.";
            }
        }

        return null;
    }

    /// <summary>
    /// A host name or address that begins with a letter, digit or colon — never a dash, which
    /// <c>ssh</c> would take for an option — and has nothing a shell could read as syntax.
    /// </summary>
    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 253)
        {
            return false;
        }

        return HostPattern().IsMatch(host) || (host.Contains(':') && !host.Contains('%') && IPAddress.TryParse(host, out _));
    }

    /// <summary>Names, <c>DOMAIN\name</c> and <c>name@domain</c>; no quotes, spaces or leading dash.</summary>
    public static bool IsValidUsername(string? username) =>
        string.IsNullOrEmpty(username) || (username.Length <= 128 && UsernamePattern().IsMatch(username));

    public RemoteConnection Clone() => (RemoteConnection)MemberwiseClone();

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9])?$")]
    private static partial Regex HostPattern();

    [GeneratedRegex(@"^[A-Za-z0-9_.][A-Za-z0-9_.\-\\@$]*$")]
    private static partial Regex UsernamePattern();
}
