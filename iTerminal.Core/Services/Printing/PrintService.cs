using iTerminal.Core.Services.Processes;
using iTerminal.Core.Services.Signing;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.Printing;

/// <summary>
/// Prints through the platform's own tools, run through <see cref="IExternalProcessRunner"/> so they can be
/// faked: Windows PowerShell's PrintManagement cmdlets on Windows, CUPS (<c>lpstat</c>, <c>lp</c>,
/// <c>cancel</c>) elsewhere. Nothing is P/Invoked.
///
/// <list type="bullet">
/// <item><b>Nothing typed becomes code.</b> CUPS values are separate arguments, behind <c>--</c> where a
/// file follows. On PowerShell a printer name or a path is embedded only as a single-quoted literal
/// (<see cref="SignatureService.Literal"/>); the extra quoting the shell's <c>PrintTo</c> verb needs is
/// why a printer name with a double quote is refused. A job id is an <see cref="int"/>, a name that
/// starts with <c>-</c> could be read as an option and is refused, and so is any control character.</item>
/// <item><b>Windows PowerShell 5.1, deliberately.</b> PrintManagement is native there; <c>pwsh</c> would
/// have to load it through a compatibility layer.</item>
/// <item><b>Windows prints text itself, hands everything else on.</b> A file with a text extension goes
/// through <c>Out-Printer</c>; any other file through the shell's <c>PrintTo</c> verb, which needs a program
/// associated with that type — and says so when there is none. CUPS takes text, PDF and images as they are.</item>
/// <item><b>A tool that does not answer is a sentence.</b> The real runner stops a tool after 30 s and
/// reports exit code -1; this service stops it after 15 s sooner, and turns that cancellation into the same
/// kind of sentence. The caller's own cancellation is not turned into anything.</item>
/// </list>
/// </summary>
public sealed partial class PrintService : IPrintService
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Extensions Windows prints as plain text; any other type is printed by the program associated with it.</summary>
    internal static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".csv", ".tsv", ".json", ".xml", ".ini", ".cfg", ".conf", ".yml", ".yaml",
        ".cs", ".py", ".js", ".ts", ".sh", ".bat", ".cmd", ".ps1",
    };

    private const int MaxCopies = 99;

    private readonly IExternalProcessRunner _runner;
    private readonly TimeSpan _timeout;
    private readonly bool _windows;

    public PrintService(IExternalProcessRunner runner)
        : this(runner, DefaultTimeout, OperatingSystem.IsWindows())
    {
    }

    internal PrintService(IExternalProcessRunner runner, TimeSpan timeout, bool windows)
    {
        _runner = runner;
        _timeout = timeout;
        _windows = windows;
    }

    public async Task<PrinterListing> ListPrintersAsync(CancellationToken cancellationToken = default)
    {
        if (_windows)
        {
            var output = await RunPowerShellAsync(
                "$ErrorActionPreference = 'Stop'; " +
                "ConvertTo-Json -InputObject @(Get-CimInstance -ClassName Win32_Printer | " +
                "Select-Object Name, Default, @{n='Status';e={if ($_.WorkOffline) {'Offline'} else {'Ready'}}}) -Compress",
                cancellationToken);
            return output.Success ? new PrinterListing(ParseWindowsPrinters(output.StandardOutput)) : new PrinterListing([], Describe(output));
        }

        var listing = await RunAsync("lpstat", ["-p", "-d"], cancellationToken);
        if (!listing.Success && !listing.StandardError.Contains("No destinations", StringComparison.OrdinalIgnoreCase)
            && !listing.StandardOutput.Contains("no system default", StringComparison.OrdinalIgnoreCase))
        {
            return new PrinterListing([], Describe(listing));
        }

        return new PrinterListing(ParseCupsPrinters(listing.StandardOutput));
    }

    public async Task<QueueListing> GetQueueAsync(string printer, CancellationToken cancellationToken = default)
    {
        if (CheckPrinter(printer) is { } refusal)
        {
            return new QueueListing([], refusal);
        }

        if (_windows)
        {
            var output = await RunPowerShellAsync(
                "$ErrorActionPreference = 'Stop'; " +
                $"ConvertTo-Json -InputObject @(Get-PrintJob -PrinterName {SignatureService.Literal(printer)} | " +
                "Select-Object Id, DocumentName, UserName, @{n='Size';e={[long]$_.Size}}, @{n='Status';e={[string]$_.JobStatus}}) -Compress",
                cancellationToken);
            return output.Success ? new QueueListing(ParseWindowsJobs(printer, output.StandardOutput)) : new QueueListing([], Describe(output));
        }

        var jobs = await RunAsync("lpstat", ["-l", "-o", printer], cancellationToken);
        return jobs.Success ? new QueueListing(ParseCupsJobs(printer, jobs.StandardOutput)) : new QueueListing([], Describe(jobs));
    }

    public async Task<PrintResult> PrintFileAsync(string printer, string filePath, int copies, CancellationToken cancellationToken = default)
    {
        if (CheckPrinter(printer) is { } refusal)
        {
            return new PrintResult(false, refusal);
        }

        if (!Path.IsPathRooted(filePath) || filePath.Any(char.IsControl) || !File.Exists(filePath))
        {
            return new PrintResult(false, "Only a file on this computer can be printed.");
        }

        copies = Math.Clamp(copies, 1, MaxCopies);
        var name = Path.GetFileName(filePath);
        if (_windows)
        {
            var literalFile = SignatureService.Literal(filePath);
            string job;
            if (TextExtensions.Contains(Path.GetExtension(filePath)))
            {
                job = $"Get-Content -LiteralPath {literalFile} -Raw | Out-Printer -Name {SignatureService.Literal(printer)}";
            }
            else if (printer.Contains('"'))
            {
                return new PrintResult(false, "A printer name with a double quote can print text files only.");
            }
            else
            {
                job = $"Start-Process -FilePath {literalFile} -Verb PrintTo -ArgumentList {SignatureService.Literal("\"" + printer + "\"")}";
            }

            var windows = await RunPowerShellAsync(
                $"$ErrorActionPreference = 'Stop'; 1..{copies.ToString(CultureInfo.InvariantCulture)} | ForEach-Object {{ {job} }}",
                cancellationToken);
            return windows.Success
                ? new PrintResult(true, $"{name} was sent to {printer}.")
                : new PrintResult(false, Describe(windows));
        }

        var output = await RunAsync("lp", ["-d", printer, "-n", copies.ToString(CultureInfo.InvariantCulture), "--", filePath], cancellationToken);
        if (!output.Success)
        {
            return new PrintResult(false, Describe(output));
        }

        var request = RequestIdPattern().Match(output.StandardOutput);
        return new PrintResult(true, request.Success ? $"{name} was sent to {printer} ({request.Groups[1].Value})." : $"{name} was sent to {printer}.");
    }

    public Task<PrintResult> HoldJobAsync(string printer, int jobId, CancellationToken cancellationToken = default) =>
        ChangeJobAsync(printer, jobId, "Suspend-PrintJob", ["-H", "hold"], "is held", cancellationToken);

    public Task<PrintResult> ResumeJobAsync(string printer, int jobId, CancellationToken cancellationToken = default) =>
        ChangeJobAsync(printer, jobId, "Resume-PrintJob", ["-H", "resume"], "goes on", cancellationToken);

    public Task<PrintResult> CancelJobAsync(string printer, int jobId, CancellationToken cancellationToken = default) =>
        ChangeJobAsync(printer, jobId, "Remove-PrintJob", null, "was cancelled", cancellationToken);

    private async Task<PrintResult> ChangeJobAsync(
        string printer, int jobId, string cmdlet, string[]? cupsOptions, string outcome, CancellationToken cancellationToken)
    {
        if (CheckPrinter(printer) is { } refusal)
        {
            return new PrintResult(false, refusal);
        }

        if (jobId < 0)
        {
            return new PrintResult(false, "That is not a job.");
        }

        var id = jobId.ToString(CultureInfo.InvariantCulture);
        ProcessOutput output;
        if (_windows)
        {
            output = await RunPowerShellAsync(
                $"$ErrorActionPreference = 'Stop'; {cmdlet} -PrinterName {SignatureService.Literal(printer)} -ID {id}",
                cancellationToken);
        }
        else
        {
            var request = printer + "-" + id;
            output = cupsOptions is null
                ? await RunAsync("cancel", [request], cancellationToken)
                : await RunAsync("lp", ["-i", request, .. cupsOptions], cancellationToken);
        }

        return output.Success ? new PrintResult(true, $"Job {id} {outcome}.") : new PrintResult(false, Describe(output));
    }

    /// <summary>The reason <paramref name="printer"/> may not be handed to a tool, or null.</summary>
    private static string? CheckPrinter(string printer) =>
        string.IsNullOrWhiteSpace(printer) ? "Choose a printer first."
        : printer.StartsWith('-') || printer.Any(char.IsControl) ? "That is not a printer name."
        : null;

    private static string Describe(ProcessOutput output) =>
        output.ExitCode == -1 && output.StandardError.Length > 0 ? output.StandardError : output.FirstErrorLine;

    private Task<ProcessOutput> RunPowerShellAsync(string script, CancellationToken cancellationToken) =>
        RunAsync("powershell", ["-NoProfile", "-NonInteractive", "-Command", script], cancellationToken);

    /// <summary>
    /// Runs a tool for at most <see cref="_timeout"/>. The runner's own ceiling (30 s) answers with exit code -1;
    /// ours is a cancellation, which is turned into the same kind of answer. The caller's cancellation passes through.
    /// </summary>
    private async Task<ProcessOutput> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(_timeout);
        try
        {
            return await _runner.RunAsync(fileName, arguments, null, bound.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProcessOutput(-1, string.Empty, $"{fileName} did not answer within {(int)_timeout.TotalSeconds} seconds.");
        }
    }

    #region Parsing
    internal static IReadOnlyList<Printer> ParseWindowsPrinters(string json) =>
        [.. Elements(json)
            .Select(e => new Printer(Text(e, "Name"), e.TryGetProperty("Default", out var d) && d.ValueKind == JsonValueKind.True, Text(e, "Status")))
            .Where(p => p.Name.Length > 0)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];

    internal static IReadOnlyList<PrintJob> ParseWindowsJobs(string printer, string json) =>
        [.. Elements(json)
            .Where(e => e.TryGetProperty("Id", out var id) && id.ValueKind == JsonValueKind.Number)
            .Select(e => new PrintJob(
                printer,
                e.GetProperty("Id").GetInt32(),
                Text(e, "DocumentName"),
                Text(e, "UserName"),
                e.TryGetProperty("Size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetInt64() : 0,
                WindowsState(Text(e, "Status"))))];

    internal static PrintJobState WindowsState(string status) =>
        status.Contains("Paused", StringComparison.OrdinalIgnoreCase) ? PrintJobState.Held
        : status.Contains("Error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Offline", StringComparison.OrdinalIgnoreCase)
            || status.Contains("PaperOut", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Blocked", StringComparison.OrdinalIgnoreCase) ? PrintJobState.Error
        : status.Contains("Printing", StringComparison.OrdinalIgnoreCase) ? PrintJobState.Printing
        : PrintJobState.Queued;

    internal static IReadOnlyList<Printer> ParseCupsPrinters(string output)
    {
        var defaultName = DefaultDestinationPattern().Match(output) is { Success: true } match ? match.Groups[1].Value : null;
        List<Printer> printers = [];
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (CupsPrinterPattern().Match(line) is { Success: true } printer)
            {
                var name = printer.Groups[1].Value;
                printers.Add(new Printer(name, name == defaultName, CupsStatus(printer.Groups[2].Value)));
            }
        }

        return [.. printers.OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static string CupsStatus(string rest) =>
        rest.Contains("disabled", StringComparison.OrdinalIgnoreCase) ? "disabled"
        : rest.Contains("now printing", StringComparison.OrdinalIgnoreCase) ? "printing"
        : rest.Contains("idle", StringComparison.OrdinalIgnoreCase) ? "idle"
        : rest.Trim().TrimEnd('.');

    /// <summary>
    /// <c>lpstat -l -o</c>: a header line per job (<c>name-id owner size date</c>) followed by indented lines.
    /// The short form does not say whether a job is held, the indented lines do (<c>job-hold-until-specified</c>).
    /// </summary>
    internal static IReadOnlyList<PrintJob> ParseCupsJobs(string printer, string output)
    {
        List<PrintJob> jobs = [];
        PrintJob? current = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = line.TrimEnd('\r');
            if (text.Length > 0 && !char.IsWhiteSpace(text[0]) && CupsJobPattern().Match(text) is { Success: true } header)
            {
                if (current is not null)
                {
                    jobs.Add(current);
                }

                current = new PrintJob(
                    printer,
                    int.Parse(header.Groups[2].Value, CultureInfo.InvariantCulture),
                    "Job " + header.Groups[2].Value,
                    header.Groups[3].Value,
                    long.TryParse(header.Groups[4].Value, CultureInfo.InvariantCulture, out var size) ? size : 0,
                    PrintJobState.Queued);
            }
            else if (current is not null && char.IsWhiteSpace(text[0]) && text.Contains("hold", StringComparison.OrdinalIgnoreCase))
            {
                current = current with { State = PrintJobState.Held };
            }
        }

        if (current is not null)
        {
            jobs.Add(current);
        }

        return jobs;
    }

    private static IEnumerable<JsonElement> Elements(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            yield break;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
                {
                    yield return element.Clone();
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                yield return root.Clone();
            }
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    [GeneratedRegex(@"^printer\s+(\S+)\s+(.*)$")]
    private static partial Regex CupsPrinterPattern();

    [GeneratedRegex(@"default destination:\s*(\S+)")]
    private static partial Regex DefaultDestinationPattern();

    [GeneratedRegex(@"^(.+)-(\d+)\s+(\S+)\s+(\d+)\s")]
    private static partial Regex CupsJobPattern();

    [GeneratedRegex(@"request id is (\S+)")]
    private static partial Regex RequestIdPattern();
    #endregion
}
