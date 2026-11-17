using iTerminal.Core.Services.Processes;
using System.Text;

namespace iTerminal.Core.Services.Archives;

/// <summary>
/// RAR archives, written by RARLAB's <c>rar</c> command-line tool. The format is proprietary and no managed
/// library writes it, so the tool is a runtime dependency the user installs (WinRAR on Windows, <c>rar</c> from
/// the package manager elsewhere); it is never shipped with iTerminal.
///
/// <para>The tool adds to an existing archive itself. A name the archive already has is replaced: RAR has no
/// "keep the archive's copy" choice, so <see cref="ArchiveFormat.Rar"/> turns that option off in the dialog.</para>
///
/// <para><b>The password goes in on stdin, never on the command line.</b> <c>-p</c> (or <c>-hp</c> when the names
/// are encrypted) with no value makes <c>rar</c> ask for it, and it reads the answer from a redirected stdin.</para>
/// </summary>
public sealed class RarArchiveWriter(IExternalProcessRunner runner, Func<string?>? locate = null)
{
    private readonly Lazy<string?> _executable = new(locate ?? FindExecutable);

    /// <summary>The <c>rar</c> executable, or null when none is installed. Looked for once.</summary>
    public string? Executable => _executable.Value;

    public bool IsAvailable => Executable is not null;

    /// <summary>
    /// <c>rar</c> on PATH, then (Windows) the WinRAR install folder, which the installer does not add to PATH.
    /// </summary>
    internal static string? FindExecutable() => RarTools.Find("rar", "Rar.exe");

    /// <summary>The switches for a run, password excluded (it goes on stdin).</summary>
    internal static List<string> BuildArguments(string archivePath, CompressionOptions options, IReadOnlyList<string> inputs)
    {
        var arguments = new List<string>
        {
            "a",
            $"-m{Math.Clamp(options.Level, 0, 5)}",
            "-r",  // folders with their contents
            "-y",  // assume yes to every question
        };

        if (options.HasPassword)
        {
            arguments.Add(options.EncryptNames ? "-hp" : "-p");
        }

        arguments.Add("--");
        arguments.Add(archivePath);
        arguments.AddRange(inputs);
        return arguments;
    }

    public async Task<CompressionResult> WriteAsync(
        string archivePath,
        IReadOnlyList<ArchiveItem> items,
        CompressionOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var executable = Executable ?? throw new InvalidOperationException("RAR archives need the rar program installed.");
        var files = items.Count(i => !i.IsFolder);
        var total = items.Where(i => !i.IsFolder).Sum(i => Math.Max(0, i.Size));

        // Names are taken from the paths given, relative to the working folder, as with 7-Zip.
        var staging = Path.Combine(Path.GetTempPath(), "iTerminal-rar-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string workingFolder;
            if (items.All(i => i.IsFolder || i.LocalPath is not null) && SevenZipArchiveWriter.CommonRoot(items) is { } root)
            {
                workingFolder = root;
            }
            else
            {
                await SevenZipArchiveWriter.StageAsync(items, staging, cancellationToken);
                workingFolder = staging;
            }

            var arguments = BuildArguments(Path.GetFullPath(archivePath), options, SevenZipArchiveWriter.TopLevelNames(items));
            var output = new SevenZipArchiveWriter.PercentageStream(percent =>
                progress?.Report(new ArchiveProgress(total * percent / 100, total, files * percent / 100, files, string.Empty)));

            var result = await runner.RunToStreamAsync(
                executable,
                arguments,
                output,
                workingFolder,
                // Always redirected and closed when there is no password: an encrypted archive being added
                // to makes rar ask for one anyway, and with stdin inherited it would wait for ever.
                async stdin =>
                {
                    if (options.HasPassword)
                    {
                        // Twice: rar asks for the password, then for it again to confirm it, and a new archive
                        // is written only when both answers agree.
                        var line = options.Password + Environment.NewLine;
                        await stdin.WriteAsync(Encoding.UTF8.GetBytes(line + line), cancellationToken);
                        await stdin.FlushAsync(cancellationToken);
                    }
                },
                cancellationToken);

            if (!result.Success)
            {
                throw new IOException(DescribeFailure(result, output.Text));
            }

            return new CompressionResult(files, 0, 0);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch
            {
                // Litter in %TEMP%, not a failure of the compression.
            }
        }
    }

    /// <summary>The first line rar calls an error, else the tool's own first line.</summary>
    internal static string DescribeFailure(ProcessOutput result, string output)
    {
        var lines = (output + "\n" + result.StandardError)
            .Split(['\r', '\n', '\b'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Only a password question, not any line that happens to name a file with "password" in it.
        if (result.ExitCode != 0 && (output + result.StandardError).Contains("enter password", StringComparison.OrdinalIgnoreCase))
        {
            return "The archive is encrypted: type its password to add to it.";
        }

        var error = lines.FirstOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase));
        return error is not null
            ? $"rar: {error}"
            : $"rar failed (exit code {result.ExitCode}): {result.FirstErrorLine}";
    }
}

/// <summary>Finds the RAR command-line programs, shared by the writer and the reader.</summary>
internal static class RarTools
{
    /// <summary>
    /// <paramref name="unixName"/> on PATH (Windows: with <c>.exe</c>), then on Windows the program in the standard
    /// WinRAR install folders, where the installer leaves it off the PATH. <paramref name="windowsName"/> is the
    /// file name there.
    /// </summary>
    internal static string? Find(string unixName, string windowsName)
    {
        var names = OperatingSystem.IsWindows() ? new[] { windowsName } : new[] { unixName };
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(folder.Trim('"'), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry.
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                var candidate = Path.Combine(Environment.GetFolderPath(root), "WinRAR", windowsName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
