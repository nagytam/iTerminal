using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;

namespace iTerminal.Core.Services.Evidence;

/// <summary>
/// Reads the origin of a new entry from the machine it is added on. Nothing here asks the person:
/// every field is a fact about the moment of capture, and a failed lookup leaves its field empty
/// rather than stopping the entry from being recorded.
/// </summary>
public static class EvidenceOriginCapture
{
    /// <summary>The origin of an entry added now, from <paramref name="sourceUrl"/> on <paramref name="sourceDrive"/>.</summary>
    public static EvidenceOrigin Capture(string sourceUrl, string sourceDrive, long? sourceSize, DateTime? sourceModifiedUtc, DateTimeOffset now) =>
        new(
            SourceUrl: sourceUrl,
            SourceDrive: sourceDrive,
            SourceSize: sourceSize,
            SourceModifiedUtc: sourceModifiedUtc,
            Machine: SafeRead(() => Environment.MachineName),
            OperatingUser: SafeRead(() => Environment.UserName),
            OperatingSystem: SafeRead(() => RuntimeInformation.OSDescription),
            AppVersion: ApplicationVersion(),
            LocalAddresses: LocalAddresses(),
            CapturedUtc: now.UtcDateTime,
            UtcOffset: now.ToString("zzz", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The version the application reports, or "unknown" when it reports none.</summary>
    public static string ApplicationVersion()
    {
        var assembly = typeof(EvidenceOriginCapture).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    /// <summary>The addresses of the machine's running network interfaces, without loopback. Empty when none can be read.</summary>
    public static IReadOnlyList<string> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address => address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6
                    && !IPAddress.IsLoopback(address))
                .Select(address => address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception exception) when (exception is NetworkInformationException or InvalidOperationException or PlatformNotSupportedException)
        {
            return [];
        }
    }

    private static string SafeRead(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException or System.Security.SecurityException)
        {
            return string.Empty;
        }
    }
}
