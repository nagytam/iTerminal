using System.Net;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Model.Remote;

/// <summary>What a saved <see cref="RemoteConnection"/> speaks.</summary>
public enum RemoteProtocol
{
    Ssh,
    PowerShell,
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
/// passphrase or a host-key question belongs.
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

    /// <summary>The port the connection actually uses.</summary>
    public int EffectivePort => Port ?? DefaultPort;

    public int DefaultPort => Protocol == RemoteProtocol.PowerShell && Transport == PowerShellTransport.WinRm ? 5985 : 22;

    public bool UsesSsh => Protocol == RemoteProtocol.Ssh || Transport == PowerShellTransport.Ssh;

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
