using System.Globalization;
using System.Text.RegularExpressions;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Mtp;

/// <summary>
/// MTP on Linux through GVfs, driven with the <c>gio</c> command line (<c>glib2</c> ships it on
/// every desktop that has a GNOME- or KDE-style volume manager). GVfs's <c>gvfsd-mtp</c> already
/// owns the device when the desktop has mounted it, which is why nothing here opens USB itself:
/// <c>mtp://&lt;host&gt;/&lt;storage&gt;/&lt;path&gt;</c> is the URI GVfs gives it.
///
/// <para>A device is whatever <c>gio mount -li</c> lists with an <c>mtp://</c> activation root; it
/// has to be unlocked and set to file transfer on the phone, and mounted (a file manager does it
/// on plug-in; <c>gio mount mtp://…</c> does too). Names are passed to <c>gio</c> as arguments,
/// never through a shell, and each path segment is percent-escaped into the URI.</para>
///
/// <para>Written against <c>gio</c>'s documented output and exercised against recorded output; it
/// has not been run against a real phone.</para>
/// </summary>
public sealed partial class GioMtpBackend(IExternalProcessRunner processRunner) : IMtpBackend
{
    private const string Gio = "gio";

    public string Unavailable => string.Empty;

    [GeneratedRegex(@"mtp://(?<host>[^/\s'""]+)/?")]
    private static partial Regex MtpRoot();

    // name <TAB> size <TAB> (type) [<TAB> attribute=value …]
    [GeneratedRegex(@"^(?<name>.+?)\t(?<size>\d+)\t\((?<type>[a-z]+)\)(?:\t(?<attributes>.*))?$")]
    private static partial Regex ListLine();

    [GeneratedRegex(@"time::modified=(?<seconds>\d+)")]
    private static partial Regex Modified();

    private static string Uri(string device, string path)
    {
        var escaped = string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(System.Uri.EscapeDataString));
        return $"mtp://{device}/{escaped}";
    }

    private static IOException Failure(ProcessOutput result) =>
        new(Describe(result));

    internal static string Describe(ProcessOutput result)
    {
        var text = result.StandardError + "\n" + result.StandardOutput;
        if (text.Contains("No such file or directory", StringComparison.OrdinalIgnoreCase)
            && text.Contains("gio", StringComparison.OrdinalIgnoreCase)
            || result.ExitCode == 127)
        {
            return "gio is not installed — install the GLib/GVfs tools (gvfs-backends) to browse a phone.";
        }

        if (text.Contains("Location is not mounted", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Operation not supported", StringComparison.OrdinalIgnoreCase))
        {
            return "The device is not mounted: unlock the phone, choose File transfer, and open it once in a file manager (or run gio mount).";
        }

        if (text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return "The device refused access: unlock the phone and allow file transfer.";
        }

        return result.FirstErrorLine;
    }

    public async Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(Gio, ["mount", "-li"], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw Failure(result);
        }

        return ParseDevices(result.StandardOutput);
    }

    /// <summary>The distinct hosts of the <c>mtp://</c> locations in <c>gio mount -li</c> output.</summary>
    internal static IReadOnlyList<string> ParseDevices(string output) =>
    [
        .. MtpRoot().Matches(output)
            .Select(m => m.Groups["host"].Value)
            .Distinct(StringComparer.Ordinal),
    ];

    public async Task<IReadOnlyList<string>> GetStoragesAsync(string device, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(Gio, ["list", $"mtp://{device}/"], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw Failure(result);
        }

        return
        [
            .. result.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];
    }

    public async Task<IReadOnlyList<MtpItem>> ListAsync(string device, string path, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            Gio,
            ["list", "-l", "-a", "time::modified", Uri(device, path) + "/"],
            cancellationToken: cancellationToken);
        if (!result.Success && result.StandardOutput.Trim().Length == 0)
        {
            throw Failure(result);
        }

        return ParseListing(result.StandardOutput);
    }

    internal static IReadOnlyList<MtpItem> ParseListing(string output)
    {
        List<MtpItem> items = [];
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ListLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            DateTime? modified = null;
            if (Modified().Match(match.Groups["attributes"].Value) is { Success: true } seconds
                && long.TryParse(seconds.Groups["seconds"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var epoch))
            {
                modified = DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime;
            }

            var isFolder = match.Groups["type"].Value is "directory" or "mountable";
            items.Add(new MtpItem(match.Groups["name"].Value, isFolder, long.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture), modified));
        }

        return items;
    }

    public async Task DownloadAsync(string device, string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunToStreamAsync(
            Gio,
            ["cat", Uri(device, path)],
            new ProgressStream(destination, progress),
            cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw Failure(result);
        }
    }

    public async Task UploadAsync(string device, string path, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        // gio save reads stdin to its end and replaces the file, creating it when it is not there.
        var result = await processRunner.RunToStreamAsync(
            Gio,
            ["save", Uri(device, path)],
            Stream.Null,
            writeStandardInput: stdin => source.CopyToAsync(new ProgressStream(stdin, progress), cancellationToken),
            cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw Failure(result);
        }
    }

    public async Task CreateFolderAsync(string device, string path, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(Gio, ["mkdir", Uri(device, path)], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw Failure(result);
        }
    }

    public async Task DeleteAsync(string device, string path, bool isFolder, CancellationToken cancellationToken)
    {
        // gio remove deletes for good — an MTP device has no trash to move to.
        var result = await processRunner.RunAsync(Gio, ["remove", Uri(device, path)], cancellationToken: cancellationToken);
        if (!result.Success)
        {
            throw Failure(result);
        }
    }
}
