using iTerminal.Core.Services.Processes;
using System.Globalization;
using System.Text;

namespace iTerminal.Core.Services.Archives;

/// <summary>
/// RAR archives read by the <c>unrar</c> command-line tool, for a machine without 7-Zip. unrar only reads, which is
/// all that is needed here: listing, extracting and streaming one entry. Like <see cref="RarArchiveWriter"/> it is a
/// runtime dependency the user installs, never shipped.
///
/// <para><b>The password goes in on stdin, never on the command line.</b> <c>-p</c> with no value makes unrar read it
/// from a redirected stdin; <c>-p-</c> says there is none, so an encrypted archive fails at once instead of waiting.</para>
/// </summary>
public sealed class UnrarArchiveReader(IExternalProcessRunner runner, Func<string?>? locate = null)
{
    private readonly Lazy<string?> _executable = new(locate ?? FindExecutable);

    /// <summary>The <c>unrar</c> executable, or null when none is installed. Looked for once.</summary>
    public string? Executable => _executable.Value;

    public bool IsAvailable => Executable is not null;

    /// <summary>
    /// <c>unrar</c> on PATH, then (Windows) the WinRAR install folder. The Windows program is <c>UnRAR.exe</c>; WinRAR
    /// keeps it off the PATH.
    /// </summary>
    internal static string? FindExecutable() => RarTools.Find("unrar", "UnRAR.exe");

    /// <summary>
    /// The archive's entries, with folders. Names are taken from <c>unrar lt</c>, one block per entry. An archive whose
    /// names are encrypted lists nothing without the password, which is reported as such rather than as an empty archive.
    /// </summary>
    public async Task<IReadOnlyList<ArchiveIndexEntry>> ListAsync(string archivePath, CancellationToken cancellationToken)
    {
        var executable = Executable ?? throw new InvalidOperationException("RAR archives need 7-Zip or unrar installed.");
        using var output = new MemoryStream();
        ProcessOutput result = await runner.RunToStreamAsync(executable, ["lt", "-p-", "--", archivePath], output, null, _ => Task.CompletedTask, cancellationToken);
        var text = Encoding.UTF8.GetString(output.ToArray());
        var entries = ParseListing(text);
        if (!result.Success || (entries.Count == 0 && text.Contains("encrypted headers", StringComparison.OrdinalIgnoreCase)))
        {
            if (text.Contains("password", StringComparison.OrdinalIgnoreCase) || result.StandardError.Contains("password", StringComparison.OrdinalIgnoreCase)
                || text.Contains("encrypted headers", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"{Path.GetFileName(archivePath)} hides its file names behind a password: unpack it with Shift+F5.");
            }

            throw new IOException(DescribeFailure(result, text));
        }

        return entries;
    }

    /// <summary>
    /// Extracts the whole archive into <paramref name="targetFolder"/>. A file the folder already has is replaced when
    /// <see cref="ExtractionOptions.Overwrite"/> is set and skipped otherwise.
    /// </summary>
    public async Task ExtractAsync(
        string archivePath,
        string targetFolder,
        ExtractionOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var executable = Executable ?? throw new InvalidOperationException("RAR archives need 7-Zip or unrar installed.");
        var destination = Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var output = new SevenZipArchiveWriter.PercentageStream(percent =>
            progress?.Report(new ArchiveProgress(percent, 100, 0, 0, string.Empty)));

        ProcessOutput result = await runner.RunToStreamAsync(
            executable,
            ["x", "-y", options.Overwrite ? "-o+" : "-o-", options.HasPassword ? "-p" : "-p-", "--", Path.GetFullPath(archivePath), destination],
            output,
            targetFolder,
            async stdin =>
            {
                if (options.HasPassword)
                {
                    var line = options.Password + Environment.NewLine;
                    await stdin.WriteAsync(Encoding.UTF8.GetBytes(line), cancellationToken);
                    await stdin.FlushAsync(cancellationToken);
                }
            },
            cancellationToken);

        if (!result.Success)
        {
            var name = Path.GetFileName(archivePath);
            if ((output.Text + "\n" + result.StandardError).Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(options.HasPassword
                    ? $"{name}: the password is wrong."
                    : $"{name} is encrypted: unpack it with Shift+F5 and the password.");
            }

            throw new IOException(DescribeFailure(result, output.Text));
        }
    }

    /// <summary>One entry's bytes, to <paramref name="destination"/>. An encrypted entry is refused, as with 7-Zip.</summary>
    public async Task StreamEntryAsync(string archivePath, string entryName, Stream destination, CancellationToken cancellationToken)
    {
        var executable = Executable ?? throw new InvalidOperationException("RAR archives need 7-Zip or unrar installed.");

        // unrar reads a name as a wildcard pattern, so a name with one could match other entries: refused rather than guessed.
        if (entryName.IndexOfAny(['*', '?', '[']) >= 0)
        {
            throw new IOException($"{Path.GetFileName(entryName)} has wildcard characters that unrar cannot read: unpack it with Shift+F5.");
        }

        ProcessOutput result = await runner.RunToStreamAsync(executable, ["p", "-inul", "-p-", "--", Path.GetFullPath(archivePath), entryName], destination, null, _ => Task.CompletedTask, cancellationToken);
        if (!result.Success)
        {
            throw new IOException(result.StandardError.Contains("password", StringComparison.OrdinalIgnoreCase)
                ? $"{Path.GetFileName(entryName)} is encrypted: unpack it with Shift+F5 and the password."
                : DescribeFailure(result, string.Empty));
        }
    }

    /// <summary>
    /// <c>unrar lt</c> writes one block per entry: <c>Name:</c>, <c>Type:</c> (<c>File</c> or <c>Directory</c>),
    /// <c>Size:</c> and <c>mtime:</c> (<c>2026-11-17 18:54:46,680353970</c>), each indented. Names keep the
    /// separators the archive holds, which are turned into <c>/</c> as the drive's paths are.
    /// </summary>
    internal static List<ArchiveIndexEntry> ParseListing(string text)
    {
        var entries = new List<ArchiveIndexEntry>();
        string? name = null;
        var isFolder = false;
        long size = 0;
        var modified = default(DateTime);

        void Flush()
        {
            if (name is not null)
            {
                var path = name.Replace('\\', '/').Trim('/');
                if (path.Length > 0)
                {
                    entries.Add(new ArchiveIndexEntry(path, name, isFolder, size, modified));
                }
            }

            name = null;
            isFolder = false;
            size = 0;
            modified = default;
        }

        foreach (var line in text.Split('\n'))
        {
            // Only the indentation is taken off: a name may end in spaces, which are part of it.
            var trimmed = line.TrimEnd('\r').TrimStart();
            if (trimmed.StartsWith("Name: ", StringComparison.Ordinal))
            {
                Flush();
                name = trimmed["Name: ".Length..];
            }
            else if (trimmed.StartsWith("Type: ", StringComparison.Ordinal))
            {
                isFolder = trimmed["Type: ".Length..].StartsWith("Dir", StringComparison.OrdinalIgnoreCase);
            }
            else if (trimmed.StartsWith("Size: ", StringComparison.Ordinal))
            {
                _ = long.TryParse(trimmed["Size: ".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
            }
            else if (trimmed.StartsWith("mtime: ", StringComparison.Ordinal) && trimmed.Length >= "mtime: yyyy-MM-dd HH:mm:ss".Length)
            {
                _ = DateTime.TryParseExact(
                    trimmed["mtime: ".Length..("mtime: ".Length + 19)], "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out modified);
            }
        }

        Flush();
        return entries;
    }

    private static string DescribeFailure(ProcessOutput result, string output)
    {
        var lines = (output + "\n" + result.StandardError)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var error = lines.FirstOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase));
        return error is not null
            ? $"unrar: {error}"
            : $"unrar failed (exit code {result.ExitCode}): {result.FirstErrorLine}";
    }
}
