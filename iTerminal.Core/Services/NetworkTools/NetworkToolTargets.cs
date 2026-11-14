using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace iTerminal.Core.Services.NetworkTools;

/// <summary>
/// Reads what the user typed for a network tool and says why it cannot be used. Nothing here touches the network, so every
/// rule is tested directly.
/// </summary>
internal static class NetworkToolTargets
{
    /// <summary>A host name or an IP address, without a scheme, a path or a space.</summary>
    internal static string ParseHost(string? target)
    {
        string host = (target ?? string.Empty).Trim();
        if (host.Length == 0)
        {
            throw new ArgumentException("Type a host name or an address first.");
        }

        if (host.Contains("://", StringComparison.Ordinal) || host.Contains('/') || host.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Type a host name or an IP address, without a web scheme, a path or a space.");
        }

        return host;
    }

    /// <summary>
    /// <c>host:port</c> or <c>[IPv6 address]:port</c>. The port must be a number from 1 to 65535.
    /// </summary>
    internal static (string Host, int Port) ParseHostAndPort(string? target)
    {
        string text = (target ?? string.Empty).Trim();
        string hostText;
        string portText;

        if (text.StartsWith('['))
        {
            int close = text.IndexOf(']', StringComparison.Ordinal);
            if (close < 0 || close + 1 >= text.Length || text[close + 1] != ':')
            {
                throw new ArgumentException("Write the address and port as [address]:port.");
            }

            hostText = text[1..close];
            portText = text[(close + 2)..];
        }
        else
        {
            int colon = text.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                throw new ArgumentException("Write the host and port as host:port, for example example.com:443.");
            }

            if (text.IndexOf(':', colon + 1) >= 0)
            {
                throw new ArgumentException("Write an IPv6 address in brackets, for example [2001:db8::1]:443.");
            }

            hostText = text[..colon];
            portText = text[(colon + 1)..];
        }

        if (!int.TryParse(portText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
        {
            throw new ArgumentException("Give a port number from 1 to 65535.");
        }

        return (ParseHost(hostText), port);
    }

    /// <summary>An address of a web page. Without a scheme, https is assumed. Only http and https are accepted.</summary>
    internal static Uri ParseWebAddress(string? target)
    {
        string text = (target ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("Type a web address first.");
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException("Type a web address such as example.com or https://example.com.");
        }

        return uri;
    }

    /// <summary>An IPv4 or IPv6 address.</summary>
    internal static IPAddress ParseIpAddress(string? target)
    {
        if (!IPAddress.TryParse((target ?? string.Empty).Trim(), out IPAddress? address))
        {
            throw new ArgumentException("Type an IPv4 or IPv6 address, for example 8.8.8.8.");
        }

        return address;
    }

    /// <summary>
    /// The usable IPv4 addresses of a subnet typed as <c>network/prefix</c>, with a prefix from 22 to 32 (at most 1024 addresses).
    /// An address whose host bits are not zero is refused, and the message names the network address.
    /// The network and broadcast addresses are left out of a subnet larger than a /31; a /31 and a /32 keep every address.
    /// </summary>
    internal static IReadOnlyList<IPAddress> ParseSubnet(string? target)
    {
        string text = (target ?? string.Empty).Trim();
        int slash = text.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0 || !IsDottedQuad(text[..slash], out IPAddress? address))
        {
            throw new ArgumentException("Type an IPv4 subnet in CIDR form, for example 192.168.1.0/24.");
        }

        if (!int.TryParse(text[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int prefix) || prefix is < 22 or > 32)
        {
            throw new ArgumentException("Give a prefix from 22 to 32: a larger subnet has more than 1024 addresses.");
        }

        uint mask = uint.MaxValue << (32 - prefix);
        uint typed = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
        uint network = typed & mask;
        if (network != typed)
        {
            // An interface address such as 192.168.1.77/24 is not a subnet: scanning its network without saying so would look like a check of that one host.
            byte[] networkBytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(networkBytes, network);
            throw new ArgumentException($"The host part of {text[..slash]}/{prefix} is not zero. Type the network address, {new IPAddress(networkBytes)}/{prefix}.");
        }

        uint count = 1u << (32 - prefix);
        var addresses = new List<IPAddress>((int)count);
        for (uint i = 0; i < count; i++)
        {
            bool networkOrBroadcast = i == 0 || i == count - 1;
            if (prefix < 31 && networkOrBroadcast)
            {
                continue;
            }

            byte[] bytes = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, network + i);
            addresses.Add(new IPAddress(bytes));
        }

        return addresses;
    }

    /// <summary>
    /// Four decimal octets, each from 0 to 255. Shorthand such as <c>192.168.1</c> is refused, because the parser would read it as
    /// another address and the scan would cover a different subnet than the one typed.
    /// </summary>
    private static bool IsDottedQuad(string text, [NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        string[] parts = text.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (string part in parts)
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int octet) || octet is < 0 or > 255)
            {
                return false;
            }
        }

        return IPAddress.TryParse(text, out address);
    }

    /// <summary>The server a whois answer refers the question to (its <c>refer:</c> line), or null when it answers itself.</summary>
    internal static string? ParseWhoisReferral(string response)
    {
        foreach (string raw in response.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("refer:", StringComparison.OrdinalIgnoreCase))
            {
                string server = line["refer:".Length..].Trim();
                return server.Length > 0 ? server : null;
            }
        }

        return null;
    }
}
