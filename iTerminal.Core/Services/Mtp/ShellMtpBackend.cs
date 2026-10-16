using System.Globalization;
using System.Text;
using System.Text.Json;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Mtp;

/// <summary>
/// MTP on Windows through the shell namespace — the same view of a phone Explorer shows under
/// This PC — driven from PowerShell's <c>Shell.Application</c>. WPD, the API underneath, is a COM
/// interface with no managed wrapper that targets plain <c>net10.0</c> (the NuGet ones need a
/// <c>-windows</c> framework, which would end the app's cross-platform build), and the shell
/// namespace needs nothing but what Windows ships.
///
/// <para>Every call is one <c>pwsh</c> (or Windows PowerShell) process that finds the device under
/// This PC by name, walks the path segment by segment, and prints JSON. Names are passed as
/// PowerShell single-quoted literals (<c>'</c> doubled), never spliced as code. Files move through
/// a temporary folder: the shell copies <em>files</em> between folders, not streams.</para>
///
/// <para><b>Not supported here, on purpose:</b> deleting. The shell's delete is a verb that asks
/// for confirmation in a window a terminal app cannot answer, and an unverified destructive call on
/// somebody's phone is worse than a message. Delete on the device in Explorer for now. Like the Linux
/// backend this has not been run against a real phone.</para>
/// </summary>
public sealed class ShellMtpBackend(IExternalProcessRunner processRunner, string? powerShell = null) : IMtpBackend
{
    private readonly string _powerShell = powerShell ?? "powershell";

    public string Unavailable => string.Empty;

    internal static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    private const string Prelude = """
        $ErrorActionPreference = 'Stop'
        $shell = New-Object -ComObject Shell.Application
        function Get-DeviceFolder([string]$device) {
          $item = $shell.NameSpace(17).Items() | Where-Object { $_.Name -eq $device } | Select-Object -First 1
          if (-not $item) { throw "Device not found: $device" }
          return $item.GetFolder
        }
        function Get-PathFolder([string]$device, [string[]]$segments) {
          $folder = Get-DeviceFolder $device
          foreach ($segment in $segments) {
            $next = $folder.Items() | Where-Object { $_.Name -eq $segment -and $_.IsFolder } | Select-Object -First 1
            if (-not $next) { throw "Folder not found: $segment" }
            $folder = $next.GetFolder
          }
          return $folder
        }
        function Write-Rows($folder) {
          $rows = @($folder.Items() | ForEach-Object {
            [pscustomobject]@{ name = $_.Name; folder = [bool]$_.IsFolder; size = [int64]$_.Size; modified = $_.ModifyDate.ToUniversalTime().ToString('o') }
          })
          ConvertTo-Json -InputObject $rows -Compress
        }
        """;

    private static string SegmentsLiteral(IEnumerable<string> segments) =>
        "@(" + string.Join(",", segments.Select(Quote)) + ")";

    private static string[] Segments(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private async Task<string> RunAsync(string body, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            _powerShell,
            ["-NoProfile", "-NonInteractive", "-Command", Prelude + "\n" + body],
            cancellationToken: cancellationToken);
        if (!result.Success)
        {
            var message = result.FirstErrorLine;
            throw new IOException(message.Contains("Device not found", StringComparison.Ordinal)
                ? "The device is not connected, or is locked: unlock the phone and choose File transfer."
                : message);
        }

        return result.StandardOutput;
    }

    public async Task<IReadOnlyList<string>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        // A portable device is the item under This PC whose shell path is a device interface path
        // (::{GUID}\\?\usb#…): drives and the user's folders have ordinary paths.
        var output = await RunAsync(
            "$names = @($shell.NameSpace(17).Items() | Where-Object { $_.IsFolder -and $_.Path.Contains('\\\\?\\') } | ForEach-Object { $_.Name })\n"
            + "ConvertTo-Json -InputObject $names -Compress",
            cancellationToken);
        return ParseNames(output);
    }

    public async Task<IReadOnlyList<string>> GetStoragesAsync(string device, CancellationToken cancellationToken)
    {
        var output = await RunAsync(
            $"$folder = Get-DeviceFolder {Quote(device)}\n"
            + "$names = @($folder.Items() | ForEach-Object { $_.Name })\n"
            + "ConvertTo-Json -InputObject $names -Compress",
            cancellationToken);
        return ParseNames(output);
    }

    internal static IReadOnlyList<string> ParseNames(string output)
    {
        var text = output.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var element = JsonDocument.Parse(text).RootElement;
        return element.ValueKind == JsonValueKind.Array
            ? [.. element.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(n => n.Length > 0)]
            : [element.GetString() ?? string.Empty];
    }

    public async Task<IReadOnlyList<MtpItem>> ListAsync(string device, string path, CancellationToken cancellationToken)
    {
        var output = await RunAsync(
            $"$folder = Get-PathFolder {Quote(device)} {SegmentsLiteral(Segments(path))}\nWrite-Rows $folder",
            cancellationToken);
        return ParseRows(output);
    }

    internal static IReadOnlyList<MtpItem> ParseRows(string output)
    {
        var text = output.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var element = JsonDocument.Parse(text).RootElement;
        if (element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<MtpItem> items = [];
        foreach (var row in element.EnumerateArray())
        {
            DateTime? modified = null;
            if (row.TryGetProperty("modified", out var stamp)
                && DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                && parsed.Year > 1970)
            {
                modified = parsed.LocalDateTime;
            }

            items.Add(new MtpItem(
                row.GetProperty("name").GetString() ?? string.Empty,
                row.GetProperty("folder").GetBoolean(),
                row.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
                modified));
        }

        return items;
    }

    private static (string Folder, string Name) SplitParent(string path)
    {
        var slash = path.TrimEnd('/').LastIndexOf('/');
        return slash < 0 ? (string.Empty, path) : (path[..slash], path[(slash + 1)..]);
    }

    private static string TemporaryFolder() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "iTerminal-mtp-" + Guid.NewGuid().ToString("N"))).FullName;

    public async Task DownloadAsync(string device, string path, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (folder, name) = SplitParent(path);
        var temporary = TemporaryFolder();
        try
        {
            // 0x14: no progress window (4) and answer "yes to all" (16). The copy can finish after the
            // call returns, so the script waits for the file to appear and stop growing.
            await RunAsync(
                $"$folder = Get-PathFolder {Quote(device)} {SegmentsLiteral(Segments(folder))}\n"
                + $"$item = $folder.Items() | Where-Object {{ $_.Name -eq {Quote(name)} -and -not $_.IsFolder }} | Select-Object -First 1\n"
                + "if (-not $item) { throw 'File not found' }\n"
                + $"$target = Join-Path {Quote(temporary)} {Quote(name)}\n"
                + $"$shell.NameSpace({Quote(temporary)}).CopyHere($item, 0x14)\n"
                + "$last = -1; $deadline = (Get-Date).AddMinutes(30)\n"
                + "while ((Get-Date) -lt $deadline) {\n"
                + "  if (Test-Path -LiteralPath $target) { $size = (Get-Item -LiteralPath $target).Length; if ($size -eq $last -and $size -ge [int64]$item.Size) { break }; $last = $size }\n"
                + "  Start-Sleep -Milliseconds 300\n"
                + "}\n"
                + "if (-not (Test-Path -LiteralPath $target)) { throw 'The device did not deliver the file' }",
                cancellationToken);

            await using var file = new FileStream(Path.Combine(temporary, name), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 16];
            long total = 0;
            int read;
            while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                total += read;
                progress?.Report(total);
            }
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    public async Task UploadAsync(string device, string path, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (folder, name) = SplitParent(path);
        var temporary = TemporaryFolder();
        try
        {
            var local = Path.Combine(temporary, name);
            await using (var file = new FileStream(local, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;
                    progress?.Report(total);
                }
            }

            await RunAsync(
                $"$folder = Get-PathFolder {Quote(device)} {SegmentsLiteral(Segments(folder))}\n"
                + $"$folder.CopyHere({Quote(local)}, 0x14)\n"
                + "$deadline = (Get-Date).AddMinutes(30); $found = $false\n"
                + "while ((Get-Date) -lt $deadline -and -not $found) {\n"
                + $"  $found = [bool]($folder.Items() | Where-Object {{ $_.Name -eq {Quote(name)} }} | Select-Object -First 1)\n"
                + "  if (-not $found) { Start-Sleep -Milliseconds 300 }\n"
                + "}\n"
                + "if (-not $found) { throw 'The device did not accept the file' }",
                cancellationToken);
        }
        finally
        {
            DeleteQuietly(temporary);
        }
    }

    public async Task CreateFolderAsync(string device, string path, CancellationToken cancellationToken)
    {
        var (folder, name) = SplitParent(path);
        await RunAsync(
            $"$folder = Get-PathFolder {Quote(device)} {SegmentsLiteral(Segments(folder))}\n"
            + $"$folder.NewFolder({Quote(name)})",
            cancellationToken);
    }

    public Task DeleteAsync(string device, string path, bool isFolder, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Deleting on a phone is not available from here yet — delete it in Explorer.");

    private static void DeleteQuietly(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
