using iTerminal.Core.Services.Processes;
using System.Text;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.Archives;

/// <summary>
/// 7z archives, written by the 7-Zip command-line tool — the same bargain the audio decoders strike
/// with ffmpeg and OCR with tesseract: no managed library writes 7z with encryption, and 7-Zip is
/// what anyone with a 7z archive has installed anyway.
///
/// <para>7-Zip adds to an existing archive itself (<c>7z a</c>), in a temporary file it moves over
/// the old one, so nothing is rewritten here. It needs the files on disk: local sources are handed
/// over by path, anything else is staged into a temporary folder first.</para>
///
/// <para><b>The password goes in on stdin, never on the command line</b>, where every other process
/// on the machine could read it. <c>-p</c> with no value makes 7-Zip ask for it, and it reads the
/// answer from a redirected stdin as readily as from a console.</para>
/// </summary>
public sealed partial class SevenZipArchiveWriter(IExternalProcessRunner runner, Func<string?>? locate = null)
{
    private readonly Lazy<string?> _executable = new(locate ?? FindExecutable);

    /// <summary>The 7-Zip executable, or null when none is installed. Looked for once.</summary>
    public string? Executable => _executable.Value;

    public bool IsAvailable => Executable is not null;

    /// <summary>
    /// <c>7z</c>, <c>7zz</c> or <c>7za</c> on PATH, then the standard Windows install folder — the
    /// 7-Zip installer does not put itself on PATH, so most Windows machines with 7-Zip have it only
    /// there.
    /// </summary>
    internal static string? FindExecutable()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "7z.exe", "7zz.exe", "7za.exe" } : ["7z", "7zz", "7za"];
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
                var candidate = Path.Combine(Environment.GetFolderPath(root), "7-Zip", "7z.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>The switches for a run, password excluded (it goes on stdin).</summary>
    internal static List<string> BuildArguments(string archivePath, CompressionOptions options, IReadOnlyList<string> inputs)
    {
        var arguments = new List<string>
        {
            "a",
            "-t7z",
            $"-mx={Math.Clamp(options.Level, 0, 9)}",
            "-bsp1",  // progress percentages on stdout
            "-bb0",
            "-bse1",  // errors on stdout too, in order with everything else
            "-y",
            // Replace a file the archive already has (q/x/y/z = 2), or keep the archive's copy (= 1);
            // either way keep what is only in the archive (p1) and add what is new (r2).
            options.ReplaceExisting ? "-up1q1r2x2y2z2" : "-up1q1r2x1y1z1",
        };

        if (options.HasPassword)
        {
            arguments.Add("-p");
            if (options.EncryptNames)
            {
                arguments.Add("-mhe=on");
            }
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
        var executable = Executable ?? throw new InvalidOperationException("7z archives need 7-Zip installed (7z on PATH).");
        var files = items.Count(i => !i.IsFolder);
        var total = items.Where(i => !i.IsFolder).Sum(i => Math.Max(0, i.Size));

        // 7-Zip names entries after the paths it is given, relative to its working folder. Local
        // items are given as-is from a common root; anything else is staged under the same names.
        var staging = Path.Combine(Path.GetTempPath(), "iTerminal-7z-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            string workingFolder;
            List<string> inputs;
            if (items.All(i => i.IsFolder || i.LocalPath is not null) && CommonRoot(items) is { } root)
            {
                workingFolder = root;
                inputs = TopLevelNames(items);
            }
            else
            {
                await StageAsync(items, staging, cancellationToken);
                workingFolder = staging;
                inputs = TopLevelNames(items);
            }

            var arguments = BuildArguments(Path.GetFullPath(archivePath), options, inputs);
            var output = new PercentageStream(percent =>
                progress?.Report(new ArchiveProgress(total * percent / 100, total, files * percent / 100, files, string.Empty)));

            var result = await runner.RunToStreamAsync(
                executable,
                arguments,
                output,
                workingFolder,
                // Always redirected, and closed empty when there is no password: an encrypted
                // archive being added to makes 7-Zip ask for one anyway, and with stdin inherited it
                // would wait on the app's own console for good. Closed, it gives up (exit 255).
                async stdin =>
                {
                    if (options.HasPassword)
                    {
                        // Twice: adding to an archive whose names are encrypted, 7-Zip asks once
                        // to open it and once more for the new entries. A new archive reads only
                        // the first line; given one line, the second question meets the end of
                        // stdin and 7-Zip gives up.
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

    /// <summary>The first line 7-Zip itself calls an error, else the tool's own first line.</summary>
    internal static string DescribeFailure(ProcessOutput result, string output)
    {
        // 255 is 7-Zip's "stopped by the user": it asked for a password stdin could not give —
        // an encrypted archive being added to without one.
        if (result.ExitCode == 255 && output.Contains("Enter password", StringComparison.OrdinalIgnoreCase))
        {
            return "The archive is encrypted: type its password to add to it.";
        }

        var lines = (output + "\n" + result.StandardError)
            .Split(['\r', '\n', '\b'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var error = lines.FirstOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase));
        return error is not null
            ? $"7-Zip: {error}"
            : $"7-Zip failed (exit code {result.ExitCode}): {result.FirstErrorLine}";
    }

    /// <summary>The folder every local item's path starts with, after its entry name is taken off.</summary>
    private static string? CommonRoot(IReadOnlyList<ArchiveItem> items)
    {
        string? root = null;
        foreach (var item in items.Where(i => i.LocalPath is not null))
        {
            var relative = item.EntryName.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar);
            var local = item.LocalPath!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!local.EndsWith(relative, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var itemRoot = local[..^relative.Length].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root is null)
            {
                root = itemRoot;
            }
            else if (!string.Equals(root, itemRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return root;
    }

    /// <summary>The distinct first segments of the entry names: what 7-Zip is told to add, recursively.</summary>
    private static List<string> TopLevelNames(IReadOnlyList<ArchiveItem> items) =>
        [.. items
            .Select(i => i.EntryName.Replace('\\', '/').TrimStart('/').Split('/')[0])
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)];

    private static async Task StageAsync(IReadOnlyList<ArchiveItem> items, string staging, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(staging);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(staging, item.EntryName.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            if (item.IsFolder)
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                await item.WriteToAsync(file, cancellationToken);
            }

            File.SetLastWriteTime(path, item.Modified);
        }
    }

    /// <summary>
    /// 7-Zip's stdout, kept as text and scanned for the "NN%" its progress line writes — over and
    /// over on one line, with backspaces in between.
    /// </summary>
    internal sealed partial class PercentageStream(Action<int> onPercent) : Stream
    {
        private readonly StringBuilder _text = new();
        private int _last = -1;

        public string Text
        {
            get
            {
                lock (_text)
                {
                    return _text.ToString();
                }
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var chunk = Encoding.UTF8.GetString(buffer, offset, count);
            lock (_text)
            {
                if (_text.Length < 64 * 1024)
                {
                    _text.Append(chunk);
                }
            }

            foreach (Match match in Percent().Matches(chunk))
            {
                var value = int.Parse(match.Groups[1].Value);
                if (value != _last && value is >= 0 and <= 100)
                {
                    _last = value;
                    onPercent(value);
                }
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.ToArray(), 0, buffer.Length);
            return ValueTask.CompletedTask;
        }

        [GeneratedRegex(@"(\d{1,3})%")]
        private static partial Regex Percent();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
