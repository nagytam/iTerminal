using System.Globalization;
using System.Text.RegularExpressions;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Scanning.Sane;

/// <summary>
/// Linux (and macOS with sane-backends installed): scanners through SANE's command-line client, <c>scanimage</c>.
/// <c>scanimage -L</c> lists the devices.
///
/// <list type="bullet">
/// <item><b>Flatbed</b> is one page: the PNG goes to stdout and is copied straight into memory. No <c>--source</c>
/// is passed, so the driver's default (the glass) is used and no driver-specific name has to match.</item>
/// <item><b>Feeder (ADF)</b> is every sheet: <c>--source ADF --batch</c> writes one PNG per sheet into a
/// temporary folder, which is read and deleted afterwards. <c>scanimage</c> exits with status 7 when the feeder
/// runs out of sheets; that is the normal end of a batch, not a failure, so it is accepted when pages exist.</item>
/// </list>
///
/// Arguments go through <see cref="IExternalProcessRunner"/>, so a device id is never quoted by hand.
/// </summary>
public sealed partial class SaneScannerBackend(IExternalProcessRunner processRunner) : IScannerBackend
{
    internal const string Tool = "scanimage";

    /// <summary>The feeder name SANE drivers use for the automatic document feeder. Some drivers call it something else.</summary>
    internal const string FeederSourceName = "ADF";

    /// <summary>The exit status <c>scanimage</c> gives when a batch runs out of documents.</summary>
    internal const int NoMoreDocuments = 7;

    /// <summary>A full page at high resolution, or a whole feeder, takes far longer than the 30 s a listing gets.</summary>
    internal static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(10);

    public async Task<ScannerListing> ListAsync(CancellationToken cancellationToken)
    {
        ProcessOutput output = await processRunner.RunAsync(Tool, ["-L"], null, cancellationToken);
        if (output.ExitCode == -1)
        {
            return new ScannerListing(true, [], $"{Tool} is not available: install SANE (the sane-utils package). {output.FirstErrorLine}");
        }

        if (!output.Success)
        {
            return new ScannerListing(true, [], output.FirstErrorLine);
        }

        IReadOnlyList<ScannerDevice> devices = ParseListing(output.StandardOutput);
        return new ScannerListing(true, devices, devices.Count == 0 ? "No scanner was found." : null);
    }

    public async Task<ScanResult> ScanAsync(ScannerDevice scanner, ScanOptions options, CancellationToken cancellationToken)
    {
        if (options.Source == ScanSource.Feeder)
        {
            return await ScanFeederAsync(scanner, options, cancellationToken);
        }

        using var stream = new MemoryStream();
        IReadOnlyList<string> arguments = Common(scanner, options,
        [
            "--format=png",
        ]);

        ProcessOutput? output = await RunWithTimeoutAsync(arguments, stream, cancellationToken);
        if (output is null)
        {
            return ScanResult.Failed($"{Tool} did not finish the scan within {ScanTimeout.TotalMinutes:0} minutes.");
        }

        if (!output.Success || stream.Length == 0)
        {
            return ScanResult.Failed(output.FirstErrorLine);
        }

        return new ScanResult([new ScanPage(stream.ToArray())], $"Scanned {scanner.Name}.");
    }

    private async Task<ScanResult> ScanFeederAsync(ScannerDevice scanner, ScanOptions options, CancellationToken cancellationToken)
    {
        string folder = Path.Combine(Path.GetTempPath(), "iterminal-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            IReadOnlyList<string> arguments = Common(scanner, options,
            [
                "--format=png",
                "--source", FeederSourceName,
                "--batch=" + Path.Combine(folder, "page%03d.png"),
            ]);

            using var discarded = new MemoryStream();
            ProcessOutput? output = await RunWithTimeoutAsync(arguments, discarded, cancellationToken);
            if (output is null)
            {
                return ScanResult.Failed($"{Tool} did not finish the feeder within {ScanTimeout.TotalMinutes:0} minutes.");
            }

            string[] files = Directory.GetFiles(folder, "page*.png");
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0)
            {
                return ScanResult.Failed(output.Success || output.ExitCode == NoMoreDocuments
                    ? "The document feeder is empty."
                    : output.FirstErrorLine);
            }

            var pages = new List<ScanPage>(files.Length);
            foreach (string file in files)
            {
                pages.Add(new ScanPage(await File.ReadAllBytesAsync(file, cancellationToken)));
            }

            return new ScanResult(pages, $"Scanned {pages.Count} page{(pages.Count == 1 ? string.Empty : "s")} from {scanner.Name}.");
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // A temporary folder that cannot be removed is left for the system to clear.
            }
        }
    }

    /// <summary>The arguments every scan shares: device, resolution, colour mode and, for the flatbed, nothing more.</summary>
    private static IReadOnlyList<string> Common(ScannerDevice scanner, ScanOptions options, IEnumerable<string> extra)
    {
        var arguments = new List<string>
        {
            "-d", scanner.Id,
            "--resolution", options.Dpi.ToString(CultureInfo.InvariantCulture),
            "--mode", ModeName(options.ColorMode),
        };
        arguments.AddRange(extra);
        return arguments;
    }

    /// <summary>Runs a scan with the scan timeout. Returns null when the timeout (not the caller) ended it.</summary>
    private async Task<ProcessOutput?> RunWithTimeoutAsync(IReadOnlyList<string> arguments, Stream destination, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ScanTimeout);
        try
        {
            return await processRunner.RunToStreamAsync(Tool, arguments, destination, null, null, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The <c>--mode</c> names SANE uses: Lineart is the black-and-white mode.</summary>
    internal static string ModeName(ScanColorMode mode) => mode switch
    {
        ScanColorMode.Grayscale => "Gray",
        ScanColorMode.BlackAndWhite => "Lineart",
        _ => "Color",
    };

    /// <summary>Reads <c>scanimage -L</c> output: one line per device, <c>device 'id' is a Vendor Model</c>.</summary>
    internal static IReadOnlyList<ScannerDevice> ParseListing(string output)
    {
        var devices = new List<ScannerDevice>();
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match match = DeviceLine().Match(line);
            if (match.Success)
            {
                devices.Add(new ScannerDevice(match.Groups["id"].Value, match.Groups["name"].Value));
            }
        }

        return devices;
    }

    [GeneratedRegex("^device '(?<id>[^']+)' is an? (?<name>.+)$")]
    private static partial Regex DeviceLine();
}
