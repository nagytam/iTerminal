using System.Net;

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
