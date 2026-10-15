using System.Text.RegularExpressions;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Drives;

/// <summary>
/// <see cref="IDriveActionsService"/> over the platform's own tools: PowerShell's storage cmdlets and
/// the shell's Eject verb on Windows, <c>udisksctl</c> on Linux. Nothing is P/Invoked — the cmdlets
/// are what Explorer's own actions end in, and a tool's output can be faked in a test.
///
/// <para>A drive letter or a path reaches a script only after it has been checked against a strict
/// pattern or quoted (<see cref="Quote"/>), and the file system is chosen from
/// <see cref="FileSystems"/>: nothing a user typed is ever spliced into a script as code.</para>
/// </summary>
public sealed partial class DriveActionsService : IDriveActionsService
{
    public static readonly IReadOnlyList<string> FileSystems = ["NTFS", "exFAT", "FAT32", "ReFS"];
    public static readonly IReadOnlyList<string> ImageExtensions = [".iso", ".vhd", ".vhdx"];

    private readonly IExternalProcessRunner _runner;
    private readonly string _powerShell;

    public DriveActionsService(IExternalProcessRunner runner, string? powerShell = null)
    {
        _runner = runner;
        _powerShell = powerShell ?? FindPowerShell();
    }

    public bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public bool CanFormat => OperatingSystem.IsWindows();

    /// <summary>Whether <paramref name="path"/> names a disk image that can be attached.</summary>
    public static bool IsImage(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // ---- eject ---------------------------------------------------------------------------------

    public Task<DriveActionResult> EjectAsync(string root, CancellationToken cancellationToken = default) =>
        OperatingSystem.IsWindows() ? EjectWindowsAsync(root, cancellationToken)
        : OperatingSystem.IsLinux() ? EjectLinuxAsync(root, cancellationToken)
        : Unsupported();

    private async Task<DriveActionResult> EjectWindowsAsync(string root, CancellationToken cancellationToken)
    {
        if (!TryLetter(root, out var letter))
        {
            return Fail("Only a drive with a letter can be ejected.");
        }

        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $l = '{{letter}}'
            $vol = Get-Volume -DriveLetter $l
            $ok = $vol.DriveType -in 'Removable', 'CD-ROM'
            if (-not $ok) { try { $ok = ((Get-Partition -DriveLetter $l | Get-Disk).BusType -eq 'USB') } catch { } }
            if (-not $ok) { Write-Output 'NOTREMOVABLE'; exit 3 }
            $item = (New-Object -ComObject Shell.Application).Namespace(17).ParseName($l + ':')
            if ($null -eq $item) { Write-Output 'NOTFOUND'; exit 5 }
            $item.InvokeVerb('Eject')
            for ($i = 0; $i -lt 40; $i++) {
                Start-Sleep -Milliseconds 250
                if (-not (Test-Path ($l + ':\'))) { Write-Output 'EJECTED'; exit 0 }
            }
            Write-Output 'BUSY'; exit 4
            """;
        var output = await RunPowerShellAsync(script, cancellationToken);
        var name = $"{letter}:";
        return output switch
        {
            { Success: true } when output.StandardOutput.Contains("EJECTED") => Ok($"{name} can be removed safely."),
            _ when output.StandardOutput.Contains("NOTREMOVABLE") => Fail($"{name} is not a removable drive."),
            _ when output.StandardOutput.Contains("NOTFOUND") => Fail($"{name} is not listed as a drive that can be ejected."),
            _ when output.StandardOutput.Contains("BUSY") => Fail($"{name} is in use. Close what is open on it and try again."),
            _ => Fail(Describe(output, $"{name} could not be ejected.")),
        };
    }

    private async Task<DriveActionResult> EjectLinuxAsync(string root, CancellationToken cancellationToken)
    {
        var source = await _runner.RunAsync("findmnt", ["-n", "-o", "SOURCE", "--target", root], cancellationToken: cancellationToken);
        var device = source.StandardOutput.Trim();
        if (!source.Success || !device.StartsWith("/dev/", StringComparison.Ordinal))
        {
            return Fail(Describe(source, $"{root} is not a drive that can be ejected."));
        }

        var unmount = await _runner.RunAsync("udisksctl", ["unmount", "-b", device], cancellationToken: cancellationToken);
        if (!unmount.Success)
        {
            return Fail(Describe(unmount, $"{root} could not be unmounted."));
        }

        // Powering the disk off is what makes it safe to pull; a partition with no parent disk (a loop
        // device) is only unmounted, which is all there is to do.
        var parent = await _runner.RunAsync("lsblk", ["-no", "PKNAME", device], cancellationToken: cancellationToken);
        var disk = parent.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (parent.Success && !string.IsNullOrEmpty(disk))
        {
            var off = await _runner.RunAsync("udisksctl", ["power-off", "-b", "/dev/" + disk], cancellationToken: cancellationToken);
            if (!off.Success)
            {
                return Ok($"{root} was unmounted ({off.FirstErrorLine}).");
            }
        }

        return Ok($"{root} can be removed safely.");
    }

    // ---- format --------------------------------------------------------------------------------

    public async Task<DriveActionResult> FormatAsync(string root, DriveFormatOptions options, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Fail("Formatting is offered on Windows only; use mkfs or your disk utility here.");
        }

        if (!TryLetter(root, out var letter))
        {
            return Fail("Only a drive with a letter can be formatted.");
        }

        var fileSystem = FileSystems.FirstOrDefault(f => string.Equals(f, options.FileSystem, StringComparison.OrdinalIgnoreCase));
        if (fileSystem is null)
        {
            return Fail($"Unknown file system '{options.FileSystem}'.");
        }

        var script = $"""
            $ErrorActionPreference = 'Stop'
            Format-Volume -DriveLetter '{letter}' -FileSystem {fileSystem} -NewFileSystemLabel {Quote(options.Label ?? string.Empty)} {(options.Quick ? string.Empty : "-Full ")}-Force -Confirm:$false | Out-Null
            Write-Output 'FORMATTED'
            """;
        var output = await RunPowerShellAsync(script, cancellationToken);
        return output.Success && output.StandardOutput.Contains("FORMATTED")
            ? Ok($"{letter}: was formatted as {fileSystem}.")
            : Fail(Describe(output, $"{letter}: could not be formatted."));
    }

    public string? WhyCannotFormat(string root, IEnumerable<string> foldersInUse)
    {
        if (!CanFormat)
        {
            return "Formatting is offered on Windows only.";
        }

        if (!TryLetter(root, out var letter))
        {
            return "Only a drive with a letter can be formatted.";
        }

        var drive = $"{letter}:\\";
        var system = Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty;
        if (string.Equals(drive, system, StringComparison.OrdinalIgnoreCase))
        {
            return "That is the system drive.";
        }

        try
        {
            var type = new DriveInfo(drive).DriveType;
            if (type is DriveType.Network or DriveType.CDRom or DriveType.Ram or DriveType.NoRootDirectory or DriveType.Unknown)
            {
                return $"A {type.ToString().ToLowerInvariant()} drive cannot be formatted here.";
            }
        }
        catch (ArgumentException)
        {
            return "That is not a drive.";
        }

        var inUse = foldersInUse.Append(AppContext.BaseDirectory).Append(Environment.CurrentDirectory);
        foreach (var folder in inUse)
        {
            if (!string.IsNullOrEmpty(folder)
                && folder.StartsWith(drive, StringComparison.OrdinalIgnoreCase))
            {
                return $"{letter}: is in use ({Path.TrimEndingDirectorySeparator(folder)}). Leave it first.";
            }
        }

        return null;
    }

    // ---- disk images ---------------------------------------------------------------------------

    public Task<DriveActionResult> MountImageAsync(string imagePath, CancellationToken cancellationToken = default) =>
        !IsImage(imagePath) ? Task.FromResult(Fail("Only .iso, .vhd and .vhdx files can be mounted."))
        : OperatingSystem.IsWindows() ? MountWindowsAsync(imagePath, cancellationToken)
        : OperatingSystem.IsLinux() ? MountLinuxAsync(imagePath, cancellationToken)
        : Unsupported();

    public Task<DriveActionResult> UnmountImageAsync(string imagePath, CancellationToken cancellationToken = default) =>
        OperatingSystem.IsWindows() ? UnmountWindowsAsync(imagePath, cancellationToken)
        : OperatingSystem.IsLinux() ? UnmountLinuxAsync(imagePath, cancellationToken)
        : Unsupported();

    private async Task<DriveActionResult> MountWindowsAsync(string imagePath, CancellationToken cancellationToken)
    {
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $image = Mount-DiskImage -ImagePath {{Quote(imagePath)}} -PassThru
            $letters = @($image | Get-Volume | Where-Object { $_.DriveLetter } | ForEach-Object { [string]$_.DriveLetter })
            Write-Output ('MOUNTED=' + ($letters -join ','))
            """;
        var output = await RunPowerShellAsync(script, cancellationToken);
        if (!output.Success)
        {
            return Fail(Describe(output, $"{Path.GetFileName(imagePath)} could not be mounted."));
        }

        var line = output.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith("MOUNTED=", StringComparison.Ordinal));
        var letters = line?["MOUNTED=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        return letters.Length > 0
            ? Ok($"{Path.GetFileName(imagePath)} is mounted as {string.Join(", ", letters.Select(l => l + ":"))}")
            : Ok($"{Path.GetFileName(imagePath)} is mounted (it has no drive letter yet).");
    }

    private async Task<DriveActionResult> UnmountWindowsAsync(string imagePath, CancellationToken cancellationToken)
    {
        var script = $"""
            $ErrorActionPreference = 'Stop'
            Dismount-DiskImage -ImagePath {Quote(imagePath)} | Out-Null
            Write-Output 'DISMOUNTED'
            """;
        var output = await RunPowerShellAsync(script, cancellationToken);
        return output.Success && output.StandardOutput.Contains("DISMOUNTED")
            ? Ok($"{Path.GetFileName(imagePath)} is unmounted.")
            : Fail(Describe(output, $"{Path.GetFileName(imagePath)} could not be unmounted."));
    }

    private async Task<DriveActionResult> MountLinuxAsync(string imagePath, CancellationToken cancellationToken)
    {
        var setup = await _runner.RunAsync("udisksctl", ["loop-setup", "-f", imagePath], cancellationToken: cancellationToken);
        var device = LoopDeviceRegex().Match(setup.StandardOutput) is { Success: true } m ? m.Value : null;
        if (!setup.Success || device is null)
        {
            return Fail(Describe(setup, $"{Path.GetFileName(imagePath)} could not be attached."));
        }

        var mount = await _runner.RunAsync("udisksctl", ["mount", "-b", device], cancellationToken: cancellationToken);
        return mount.Success
            ? Ok(mount.StandardOutput.Trim().Length > 0 ? mount.StandardOutput.Trim() : $"{Path.GetFileName(imagePath)} is mounted.")
            : Fail(Describe(mount, $"{Path.GetFileName(imagePath)} was attached as {device} but not mounted."));
    }

    private async Task<DriveActionResult> UnmountLinuxAsync(string imagePath, CancellationToken cancellationToken)
    {
        var find = await _runner.RunAsync("losetup", ["-j", imagePath], cancellationToken: cancellationToken);
        var device = LoopDeviceRegex().Match(find.StandardOutput) is { Success: true } m ? m.Value : null;
        if (device is null)
        {
            return Fail($"{Path.GetFileName(imagePath)} is not mounted.");
        }

        // A loop device that is attached but not mounted makes unmount fail; the delete is what matters.
        await _runner.RunAsync("udisksctl", ["unmount", "-b", device], cancellationToken: cancellationToken);
        var delete = await _runner.RunAsync("udisksctl", ["loop-delete", "-b", device], cancellationToken: cancellationToken);
        return delete.Success
            ? Ok($"{Path.GetFileName(imagePath)} is unmounted.")
            : Fail(Describe(delete, $"{Path.GetFileName(imagePath)} could not be unmounted."));
    }

    // ---- BitLocker -----------------------------------------------------------------------------

    public async Task<BitLockerStatus> GetBitLockerStatusAsync(string root, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new BitLockerStatus(BitLockerState.NotApplicable, "BitLocker is a Windows feature.");
        }

        if (!TryLetter(root, out var letter))
        {
            return new BitLockerStatus(BitLockerState.NotApplicable, "Not a lettered drive.");
        }

        // The shell's own property: the one Explorer draws its padlock overlay from, and — unlike
        // Get-BitLockerVolume and manage-bde — readable without an elevated session.
        var script = $$"""
            $v = (New-Object -ComObject Shell.Application).NameSpace('{{letter}}:\').Self.ExtendedProperty('System.Volume.BitLockerProtection')
            Write-Output ('BL=' + $v)
            """;
        var output = await RunPowerShellAsync(script, cancellationToken);
        return ParseBitLocker(output);
    }

    internal static BitLockerStatus ParseBitLocker(ProcessOutput output)
    {
        if (!output.Success)
        {
            return new BitLockerStatus(BitLockerState.Unavailable, "Could not be read.");
        }

        var line = output.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.StartsWith("BL=", StringComparison.Ordinal));
        var text = line?["BL=".Length..].Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return new BitLockerStatus(BitLockerState.Unavailable, "Not reported for this drive.");
        }

        return text switch
        {
            "0" => new BitLockerStatus(BitLockerState.Off, "Off (not encrypted)"),
            "1" => new BitLockerStatus(BitLockerState.On, "On (protected)"),
            _ => new BitLockerStatus(BitLockerState.Unknown, $"Unknown state ({text})"),
        };
    }

    // ---- plumbing ------------------------------------------------------------------------------

    private Task<ProcessOutput> RunPowerShellAsync(string script, CancellationToken cancellationToken) =>
        _runner.RunAsync(_powerShell, ["-NoProfile", "-NonInteractive", "-Command", script], cancellationToken: cancellationToken);

    /// <summary>A single-quoted PowerShell literal: the only escape inside one is doubling the quote.</summary>
    internal static string Quote(string value) => "'" + value.Replace("'", "''").Replace("\r", " ").Replace("\n", " ") + "'";

    internal static bool TryLetter(string root, out char letter)
    {
        letter = default;
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        var match = DriveRootRegex().Match(root.Trim());
        if (!match.Success)
        {
            return false;
        }

        letter = char.ToUpperInvariant(match.Groups[1].Value[0]);
        return true;
    }

    private static string FindPowerShell()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var names = OperatingSystem.IsWindows() ? new[] { "pwsh.exe" } : new[] { "pwsh" };
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    if (File.Exists(Path.Combine(folder, name)))
                    {
                        return name;
                    }
                }
                catch (ArgumentException)
                {
                }
            }
        }

        return OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
    }

    /// <summary>The tool's own sentence when it gave one, with the common "needs administrator" case said plainly.</summary>
    private static string Describe(ProcessOutput output, string fallback)
    {
        var text = (output.StandardError + "\n" + output.StandardOutput);
        if (text.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("requires elevation", StringComparison.OrdinalIgnoreCase)
            || text.Contains("administrator", StringComparison.OrdinalIgnoreCase))
        {
            return fallback + " It needs an administrator: start iTerminal as administrator.";
        }

        var line = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => !l.StartsWith("At line", StringComparison.Ordinal) && l.Length > 0);
        return output.ExitCode == -1 || line is null ? $"{fallback} ({line ?? $"exit code {output.ExitCode}"})" : $"{fallback} {Truncate(line)}";
    }

    private static string Truncate(string text) => text.Length <= 160 ? text : text[..157] + "...";

    private static DriveActionResult Ok(string message) => new(true, message);

    private static DriveActionResult Fail(string message) => new(false, message);

    private static Task<DriveActionResult> Unsupported() =>
        Task.FromResult(Fail("Drive actions are not available on this system."));

    [GeneratedRegex(@"^([A-Za-z]):?[\\/]?$")]
    private static partial Regex DriveRootRegex();

    [GeneratedRegex(@"/dev/loop\d+")]
    private static partial Regex LoopDeviceRegex();
}
