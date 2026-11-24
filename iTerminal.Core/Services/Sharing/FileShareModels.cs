using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace iTerminal.Core.Services.Sharing;

public enum FileShareStatus
{
    Stopped,
    Starting,
    Running,
    Failed,
}

/// <summary>
/// The share's state, replaced whole on every change. The dialog reads it on the idle pass and paints the matrix in
/// its draw; nothing in it is a live object the worker keeps changing.
/// </summary>
#pragma warning disable S2368 // A square module grid is the natural shape here; a jagged array would make the code harder to read.
public sealed record FileShareState(FileShareStatus Status, string? Url, bool[,]? QrModules, int Requests, string? Message)
#pragma warning restore S2368
{
    public static FileShareState Stopped { get; } = new(FileShareStatus.Stopped, null, null, 0, null);
}

/// <summary>The outcome of a start: the URL when it runs, or the resource key of the reason when it does not.</summary>
public sealed record FileShareStartResult(bool Started, string? Url, string? FailureKey)
{
    public static FileShareStartResult Failed(string key) => new(false, null, key);
}

/// <summary>An IPv4 address of this machine, and whether its interface has a default gateway.</summary>
public sealed record LanAddress(IPAddress Address, bool HasDefaultGateway);

/// <summary>Lists this machine's usable addresses. The production source reads the network interfaces.</summary>
public interface IFileShareAddressSource
{
    IReadOnlyList<LanAddress> Candidates();
}

/// <summary>Reads the active network interfaces: up, not loopback, with an IPv4 address.</summary>
public sealed class NetworkInterfaceAddressSource : IFileShareAddressSource
{
    public IReadOnlyList<LanAddress> Candidates()
    {
        List<LanAddress> result = [];
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            bool gateway = properties.GatewayAddresses.Any(entry =>
                entry.Address.AddressFamily == AddressFamily.InterNetwork && !entry.Address.Equals(IPAddress.Any));
            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    result.Add(new LanAddress(unicast.Address, gateway));
                }
            }
        }

        return result;
    }
}

/// <summary>Chooses the address the share is reached on.</summary>
public static class FileShareAddress
{
    /// <summary>
    /// The address of an interface with a default gateway, else the first private (RFC 1918) address. Loopback and
    /// link-local addresses are never chosen. Null when there is none.
    /// </summary>
    public static IPAddress? Choose(IEnumerable<LanAddress> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        List<LanAddress> usable = candidates
            .Where(candidate => candidate.Address.AddressFamily == AddressFamily.InterNetwork)
            .Where(candidate => !IPAddress.IsLoopback(candidate.Address))
            .Where(candidate => !IsLinkLocal(candidate.Address))
            .ToList();

        return usable.FirstOrDefault(candidate => candidate.HasDefaultGateway && IsPrivate(candidate.Address))?.Address
            ?? usable.FirstOrDefault(candidate => IsPrivate(candidate.Address))?.Address;
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool IsPrivate(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }
}

/// <summary>A new random token for one session: 16 bytes, base64url, never stored.</summary>
internal static class FileShareToken
{
    public static string Create() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
