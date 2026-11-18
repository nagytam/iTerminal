namespace iTerminal.Core.Model.Network;

/// <summary>
/// The password-safe id of a login typed on the folder selector's 7-SMB page. The entry is keyed by host and
/// user rather than by a connection profile, so the page can list the host's shares with it at every scan and
/// the drive can find the password when a share is opened as that user.
/// </summary>
public static class SmbLoginKey
{
    private const string Prefix = "smb-login:";

    /// <summary>The id for <paramref name="username"/> logging in to <paramref name="host"/>. Host names are case-insensitive.</summary>
    public static string Id(string host, string username) => Prefix + host.Trim().ToLowerInvariant() + "\\" + username;

    /// <summary>Reads the host and user back from an id made by <see cref="Id"/>. False for any other id.</summary>
    public static bool TryParse(string id, out string host, out string username)
    {
        host = string.Empty;
        username = string.Empty;
        if (!id.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = id[Prefix.Length..];
        var separator = rest.IndexOf('\\');
        if (separator <= 0 || separator == rest.Length - 1)
        {
            return false;
        }

        host = rest[..separator];
        username = rest[(separator + 1)..];
        return true;
    }
}
