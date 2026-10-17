using System.Diagnostics;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.Defender;

/// <summary>How a malware scan ended.</summary>
public enum ScanOutcome
{
    /// <summary>The scanner looked and found nothing.</summary>
    Clean,

    /// <summary>The scanner reported threats (and removed or quarantined them, as its policy says).</summary>
    ThreatsFound,

    /// <summary>The scanner could not be run, or ended without a verdict.</summary>
    Failed,
}

/// <summary>The verdict of one scan and the line to tell the user.</summary>
public sealed record ScanResult(ScanOutcome Outcome, string Message);

/// <summary>Scans files and folders with the operating system's own anti-malware program.</summary>
public interface IMalwareScanService
{
    /// <summary>True when a scanner is installed here — Microsoft Defender on Windows, nothing elsewhere.</summary>
    bool IsAvailable { get; }

    /// <summary>Scans one file or folder to completion. Never throws for a scanner problem; that is a <see cref="ScanOutcome.Failed"/> result.</summary>
    Task<ScanResult> ScanAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IMalwareScanService"/> over <c>MpCmdRun.exe</c>, the command-line client of Microsoft
/// Defender: <c>-Scan -ScanType 3 -File &lt;path&gt;</c> is a custom scan of that path. It runs
/// without the runner's 30-second ceiling because scanning a folder takes as long as it takes; the
/// caller's token ends it.
/// </summary>
public sealed partial class DefenderScanService(Func<string?>? locate = null) : IMalwareScanService
{
    private readonly Func<string?> _locate = locate ?? LocateMpCmdRun;

    public bool IsAvailable => _locate() is not null;

    public async Task<ScanResult> ScanAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_locate() is not { } tool)
        {
            return new ScanResult(ScanOutcome.Failed, "Microsoft Defender is not available on this machine.");
        }

        Process? process = null;
        try
        {
            var info = new ProcessStartInfo(tool)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in new[] { "-Scan", "-ScanType", "3", "-File", path })
            {
                info.ArgumentList.Add(argument);
            }

            process = Process.Start(info) ?? throw new InvalidOperationException("MpCmdRun.exe did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return Interpret(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process?.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }

            throw;
        }
        catch (Exception exception)
        {
            return new ScanResult(ScanOutcome.Failed, exception.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Turns MpCmdRun's exit code and text into a verdict. It exits 0 when the scan finished, 2 when
    /// it found threats or failed; the text says which ("found no threats", "found 1 threats").
    /// </summary>
    public static ScanResult Interpret(int exitCode, string standardOutput, string standardError)
    {
        var found = ThreatCount().Match(standardOutput);
        if (found.Success)
        {
            return found.Groups[1].Value == "no"
                ? new ScanResult(ScanOutcome.Clean, "No threats found.")
                : new ScanResult(ScanOutcome.ThreatsFound, $"{found.Groups[1].Value} threat(s) found. Open Windows Security for details.");
        }

        if (exitCode == 0)
        {
            return new ScanResult(ScanOutcome.Clean, "No threats found.");
        }

        var detail = (standardError + "\n" + standardOutput)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? $"exit code {exitCode}";
        return new ScanResult(ScanOutcome.Failed, detail);
    }

    /// <summary>The newest platform copy of the client (Defender updates itself there), else the one shipped with Windows.</summary>
    public static string? LocateMpCmdRun()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var platform = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows Defender", "Platform");
        try
        {
            if (Directory.Exists(platform))
            {
                var newest = Directory.GetDirectories(platform)
                    .OrderByDescending(directory => Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)
                    .Select(directory => Path.Combine(directory, "MpCmdRun.exe"))
                    .FirstOrDefault(File.Exists);
                if (newest is not null)
                {
                    return newest;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var shipped = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Windows Defender", "MpCmdRun.exe");
        return File.Exists(shipped) ? shipped : null;
    }

    [GeneratedRegex(@"found\s+(no|\d+)\s+threats?", RegexOptions.IgnoreCase)]
    private static partial Regex ThreatCount();
}
