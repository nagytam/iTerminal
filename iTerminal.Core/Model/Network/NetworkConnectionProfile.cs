namespace iTerminal.Core.Model.Network;

public class NetworkConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public NetworkProtocol Protocol { get; set; }
    public string Host { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string Username { get; set; } = string.Empty;

    /// <summary>SMB only.</summary>
    public string? ShareName { get; set; }

    /// <summary>SSH only.</summary>
    public SshAuthMethod? SshAuthMethod { get; set; }

    /// <summary>SSH private-key auth only.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Whether a secret for this profile is saved in <see cref="Services.IPasswordManager"/>.</summary>
    public bool HasSavedCredential { get; set; }
}
