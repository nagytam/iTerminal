using iTerminal.Core.Services.Processes;
using Serilog;
using System.Globalization;
using System.Text;

namespace iTerminal.Core.Services.Origin;

/// <summary>
/// Where a downloaded file came from, as the system recorded it.
/// </summary>
/// <param name="ZoneId">Windows Mark-of-the-Web zone: 0 local, 1 intranet, 2 trusted, 3 internet, 4 restricted.</param>
/// <param name="ReferrerUrl">The page that linked to the file.</param>
/// <param name="HostUrl">The address the file was fetched from (Windows).</param>
/// <param name="OriginUrl">The address the file was fetched from (<c>user.xdg.origin.url</c>).</param>
/// <param name="Quarantine">The macOS <c>com.apple.quarantine</c> value.</param>
public sealed record FileOrigin(int? ZoneId, string? ReferrerUrl, string? HostUrl, string? OriginUrl, string? Quarantine)
{
    /// <summary>The address to show: where it was fetched from, else the page that linked to it.</summary>
    public string? Source => HostUrl ?? OriginUrl;

    /// <summary>The zone in words; null where the system has no zone.</summary>
    public string? ZoneName => ZoneId switch
    {
        null => null,
        0 => "Local computer",
        1 => "Local intranet",
        2 => "Trusted sites",
        3 => "Internet",
        4 => "Restricted sites",
        { } other => $"Zone {other}",
    };
}

/// <summary>
/// Reads and removes the "came from the internet" mark of a local file: the <c>Zone.Identifier</c> NTFS stream on Windows, the
/// <c>user.xdg.*</c> and <c>com.apple.quarantine</c> extended attributes elsewhere. Async by contract — a read touches the disk
/// or runs a tool. Nothing here ever opens an address it finds.
/// </summary>
public interface IFileOriginService
{
    /// <summary>The mark of a file; null when there is none, or the system cannot say.</summary>
    Task<FileOrigin?> ReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Removes the mark (like PowerShell Unblock-File). True when something was removed or nothing was there.</summary>
    Task<bool> RemoveAsync(string path, CancellationToken cancellationToken = default);
}

public sealed class FileOriginService(IExternalProcessRunner runner, ILogger? logger = null) : IFileOriginService
{
    internal const int MaxStreamChars = 65536;
    internal const string ZoneStream = ":Zone.Identifier";
    internal const string OriginAttribute = "user.xdg.origin.url";
    internal const string ReferrerAttribute = "user.xdg.referrer.url";
    internal const string QuarantineAttribute = "com.apple.quarantine";

    /// <summary>Reads the <c>[ZoneTransfer]</c> section of a Zone.Identifier stream.</summary>
    internal static FileOrigin? ParseZoneIdentifier(string text)
    {
        int? zone = null;
        string? referrer = null;
        string? host = null;
        bool inSection = false;
        foreach (string raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.StartsWith('['))
            {
                inSection = line.Equals("[ZoneTransfer]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            int equals = line.IndexOf('=');
            if (!inSection || equals <= 0)
            {
                continue;
            }

            string name = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();
            if (name.Equals("ZoneId", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
            {
                zone = id;
            }
            else if (name.Equals("ReferrerUrl", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                referrer = Clean(value);
            }
            else if (name.Equals("HostUrl", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
            {
                host = Clean(value);
            }
        }

        return zone is null && referrer is null && host is null ? null : new FileOrigin(zone, referrer, host, null, null);
    }

    public async Task<FileOrigin?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsPlainFile(path))
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return await Task.Run(() => ReadZoneStream(path), cancellationToken);
        }

        string? origin = await GetAttributeAsync(path, OriginAttribute, cancellationToken);
        string? referrer = await GetAttributeAsync(path, ReferrerAttribute, cancellationToken);
        string? quarantine = OperatingSystem.IsMacOS() ? await GetAttributeAsync(path, QuarantineAttribute, cancellationToken) : null;
        return origin is null && referrer is null && quarantine is null ? null : new FileOrigin(null, referrer, null, origin, quarantine);
    }

    public async Task<bool> RemoveAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsPlainFile(path))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return await Task.Run(() => DeleteZoneStream(path), cancellationToken);
        }

        string[] names = OperatingSystem.IsMacOS()
            ? [OriginAttribute, ReferrerAttribute, QuarantineAttribute]
            : [OriginAttribute, ReferrerAttribute];
        bool ok = true;
        foreach (string name in names)
        {
            if (await GetAttributeAsync(path, name, cancellationToken) is null)
            {
                continue;
            }

            ProcessOutput output = OperatingSystem.IsMacOS()
                ? await runner.RunAsync("xattr", ["-d", name, "--", path], null, cancellationToken)
                : await runner.RunAsync("setfattr", ["-x", name, "--", path], null, cancellationToken);
            if (!output.Success)
            {
                logger?.Warning("Removing {Attribute} from {Path} failed: {Error}", name, path, output.FirstErrorLine);
                ok = false;
            }
        }

        return ok;
    }

    /// <summary>A file, and not a link to one: the dialog shows the link itself, so what is behind it is not its mark.</summary>
    private static bool IsPlainFile(string path)
    {
        try
        {
            FileInfo info = new(path);
            return info.Exists && info.LinkTarget is null && !info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The text with control characters dropped (the file's creator wrote it) and cut to a sane length.</summary>
    internal static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string cleaned = new(value.Where(c => !char.IsControl(c)).ToArray());
        cleaned = cleaned.Length > 2048 ? cleaned[..2048] : cleaned;
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static FileOrigin? ReadZoneStream(string path)
    {
        try
        {
            using FileStream stream = new(path + ZoneStream, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream, Encoding.UTF8, true);
            char[] buffer = new char[MaxStreamChars];
            int read = reader.ReadBlock(buffer, 0, buffer.Length);
            return ParseZoneIdentifier(new string(buffer, 0, read));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // No stream (or a file system without them): the file has no mark.
            return null;
        }
    }

    private bool DeleteZoneStream(string path)
    {
        try
        {
            if (!File.Exists(path + ZoneStream))
            {
                return true;
            }

            File.Delete(path + ZoneStream);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            logger?.Warning(exception, "Removing the mark of {Path} failed", path);
            return false;
        }
    }

    /// <summary>One attribute's value; null when it is not set or the tool is missing.</summary>
    private async Task<string?> GetAttributeAsync(string path, string name, CancellationToken cancellationToken)
    {
        ProcessOutput output = OperatingSystem.IsMacOS()
            ? await runner.RunAsync("xattr", ["-p", name, "--", path], null, cancellationToken)
            : await runner.RunAsync("getfattr", ["--absolute-names", "--only-values", "-n", name, "--", path], null, cancellationToken);
        string? value = Clean(output.StandardOutput.Trim().Trim('"'));
        return output.Success ? value : null;
    }
}
