using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using iTerminal.Core.Services.Processes;
using System.Text;

namespace iTerminal.Core.Services.Archives;

/// <summary>What the Extract dialog asked for.</summary>
/// <param name="Password">Null or empty when the archive is not encrypted (or the user does not know yet).</param>
/// <param name="Overwrite">Whether a file already in the target folder is replaced (true) or kept, the archive's copy skipped (false).</param>
public sealed record ExtractionOptions(string? Password = null, bool Overwrite = false)
{
    public bool HasPassword => !string.IsNullOrEmpty(Password);
}

/// <summary>What an extraction did.</summary>
/// <param name="Skipped">Files left out because the target already had them and overwriting was off.</param>
/// <param name="Unsafe">Entries refused because their path led outside the target folder (<c>../../x</c>).</param>
public sealed record ExtractionResult(int Extracted, int Skipped, int Unsafe = 0);

/// <summary>Unpacks an archive into a folder: Shift+F5.</summary>
public interface IArchiveExtractionService
{
    /// <summary>Whether <paramref name="fileName"/> is an archive this app can unpack at all (7-Zip's formats count, installed or not).</summary>
    bool IsArchive(string fileName);

    /// <summary>Why <paramref name="fileName"/> cannot be unpacked on this machine, or null when it can.</summary>
    string? WhyCannotExtract(string fileName);

    /// <summary>The archive's name without its archive ending — the folder "into a folder of its own" creates.</summary>
    string FolderNameFor(string fileName);

    Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string targetFolder,
        ExtractionOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// See <see cref="IArchiveExtractionService"/>. The formats Alt+F5 writes in-process — ZIP and the
/// TAR family — are read in-process too, with SharpZipLib (so an AES-encrypted ZIP opens, which the
/// BCL cannot do), as are a lone <c>.gz</c> or <c>.bz2</c>. Everything else 7-Zip reads — 7z, RAR,
/// XZ, ISO, CAB — goes to the 7-Zip command-line tool when it is installed.
///
/// <para><b>Nothing is written outside the target folder.</b> An entry named <c>../../evil.exe</c>
/// (the "zip slip") is refused and counted, not followed. 7-Zip refuses such paths itself.</para>
/// </summary>
public sealed class ArchiveExtractionService(IExternalProcessRunner runner, Func<string?>? locate = null)
    : IArchiveExtractionService
{
    private readonly Lazy<string?> _sevenZip = new(locate ?? SevenZipArchiveWriter.FindExecutable);

    /// <summary>Endings read in-process, longest first so <c>.tar.gz</c> is not taken for <c>.gz</c>.</summary>
    private static readonly string[] ManagedEndings = [".tar.bz2", ".tar.gz", ".tbz2", ".tgz", ".tbz", ".tar", ".zip", ".gz", ".bz2"];

    /// <summary>Endings only 7-Zip reads.</summary>
    private static readonly string[] SevenZipEndings = [".tar.xz", ".txz", ".7z", ".rar", ".xz", ".iso", ".cab", ".lzma"];

    public string? SevenZipExecutable => _sevenZip.Value;

    private static string? EndingOf(string fileName, IEnumerable<string> endings) =>
        endings.OrderByDescending(e => e.Length).FirstOrDefault(e => fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    private static string? ManagedEnding(string fileName) => EndingOf(fileName, ManagedEndings);

    private static string? SevenZipEnding(string fileName) =>
        ManagedEnding(fileName) is null ? EndingOf(fileName, SevenZipEndings) : null;

    public bool IsArchive(string fileName) => ManagedEnding(fileName) is not null || SevenZipEnding(fileName) is not null;

    public string? WhyCannotExtract(string fileName)
    {
        if (!IsArchive(fileName))
        {
            return $"{Path.GetFileName(fileName)} is not an archive this app can unpack.";
        }

        return SevenZipEnding(fileName) is { } ending && SevenZipExecutable is null
            ? $"{ending} archives need 7-Zip installed."
            : null;
    }

    public string FolderNameFor(string fileName)
    {
        var name = Path.GetFileName(fileName.TrimEnd('/', '\\'));
        var ending = ManagedEnding(name) ?? SevenZipEnding(name);
        var folder = ending is null ? Path.GetFileNameWithoutExtension(name) : name[..^ending.Length];
        return string.IsNullOrWhiteSpace(folder) ? "extracted" : folder;
    }

    public async Task<ExtractionResult> ExtractAsync(
        string archivePath,
        string targetFolder,
        ExtractionOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (WhyCannotExtract(archivePath) is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        Directory.CreateDirectory(targetFolder);
        var ending = ManagedEnding(archivePath);
        return ending switch
        {
            ".zip" => await Task.Run(() => ExtractZip(archivePath, targetFolder, options, progress, cancellationToken), cancellationToken),
            ".tar" or ".tar.gz" or ".tgz" or ".tar.bz2" or ".tbz2" or ".tbz" =>
                await Task.Run(() => ExtractTar(archivePath, ending, targetFolder, options, progress, cancellationToken), cancellationToken),
            ".gz" or ".bz2" => await Task.Run(() => ExtractSingle(archivePath, ending, targetFolder, options, progress, cancellationToken), cancellationToken),
            _ => await ExtractWithSevenZipAsync(archivePath, targetFolder, options, progress, cancellationToken),
        };
    }

    /// <summary>
    /// Where <paramref name="entryName"/> lands under <paramref name="targetFolder"/>, or null when
    /// it would land outside it.
    /// </summary>
    internal static string? SafePath(string targetFolder, string entryName)
    {
        var root = Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var relative = entryName.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0 || Path.IsPathRooted(relative) || relative.Contains(':'))
        {
            return null;
        }

        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? full
            : null;
    }

    #region ZIP
    private static ExtractionResult ExtractZip(
        string archivePath, string targetFolder, ExtractionOptions options, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        using var zip = new ZipFile(archivePath);
        if (options.HasPassword)
        {
            zip.Password = options.Password;
        }

        var entries = zip.Cast<ZipEntry>().ToList();
        var total = entries.Where(e => e.IsFile).Sum(e => Math.Max(0, e.Size));
        var files = entries.Count(e => e.IsFile);
        long done = 0;
        int extracted = 0, skipped = 0, unsafePaths = 0, itemsDone = 0;
        var archiveName = Path.GetFileName(archivePath);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = SafePath(targetFolder, entry.Name);
            if (path is null)
            {
                unsafePaths++;
                continue;
            }

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(path);
                continue;
            }

            if (!entry.IsFile)
            {
                continue;
            }

            progress?.Report(new ArchiveProgress(done, total, itemsDone, files, entry.Name));
            if (File.Exists(path) && !options.Overwrite)
            {
                skipped++;
                itemsDone++;
                done += Math.Max(0, entry.Size);
                continue;
            }

            if (entry.IsCrypted && !options.HasPassword)
            {
                throw new InvalidOperationException($"{archiveName} is encrypted: type its password.");
            }

            Stream input;
            try
            {
                input = zip.GetInputStream(entry);
            }
            catch (ZipException exception) when (entry.IsCrypted)
            {
                throw new InvalidOperationException($"The password does not open {archiveName}.", exception);
            }

            var start = done;
            WriteFile(path, input, entry.DateTime, bytes => progress?.Report(new ArchiveProgress(start + bytes, total, itemsDone, files, entry.Name)), cancellationToken, archiveName, entry.IsCrypted);
            done += Math.Max(0, entry.Size);
            extracted++;
            itemsDone++;
        }

        progress?.Report(new ArchiveProgress(total, total, itemsDone, files, string.Empty));
        return new ExtractionResult(extracted, skipped, unsafePaths);
    }
    #endregion

    #region TAR family and single files
    private static ExtractionResult ExtractTar(
        string archivePath, string ending, string targetFolder, ExtractionOptions options, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        using var decompressed = Decompress(file, ending);
        using var tar = new TarInputStream(decompressed, Encoding.UTF8) { IsStreamOwner = false };
        int extracted = 0, skipped = 0, unsafePaths = 0, items = 0;

        // The entry count is unknown until the end, so progress is how far through the archive
        // file the reader has got — which, compressed or not, tracks the work.
        void Report(string entry) => progress?.Report(new ArchiveProgress(file.Position, file.Length, items, 0, entry));

        while (tar.GetNextEntry() is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(entry.Name);
            var path = SafePath(targetFolder, entry.Name);
            if (path is null)
            {
                unsafePaths++;
                continue;
            }

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(path);
                continue;
            }

            if (entry.TarHeader.TypeFlag is not (TarHeader.LF_NORMAL or TarHeader.LF_OLDNORM))
            {
                // Links and devices: a symlink in an archive pointing anywhere is the same attack as
                // a "../" name, by another route. Left out.
                continue;
            }

            items++;
            if (File.Exists(path) && !options.Overwrite)
            {
                skipped++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
            {
                tar.CopyEntryContents(output);
            }

            TrySetTime(path, entry.ModTime.ToLocalTime());
            extracted++;
        }

        Report(string.Empty);
        return new ExtractionResult(extracted, skipped, unsafePaths);
    }

    private static Stream Decompress(Stream file, string ending) => ending switch
    {
        ".tar.gz" or ".tgz" or ".gz" => new GZipInputStream(file) { IsStreamOwner = false },
        ".tar.bz2" or ".tbz2" or ".tbz" or ".bz2" => new BZip2InputStream(file) { IsStreamOwner = false },
        _ => new NonClosingStream(file),
    };

    /// <summary>A lone <c>notes.txt.gz</c> holds one file: <c>notes.txt</c>.</summary>
    private static ExtractionResult ExtractSingle(
        string archivePath, string ending, string targetFolder, ExtractionOptions options, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(archivePath)[..^ending.Length];
        var path = SafePath(targetFolder, string.IsNullOrEmpty(name) ? "extracted" : name)!;
        if (File.Exists(path) && !options.Overwrite)
        {
            return new ExtractionResult(0, 1);
        }

        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        using var decompressed = Decompress(file, ending);
        WriteFile(path, decompressed, File.GetLastWriteTime(archivePath),
            _ => progress?.Report(new ArchiveProgress(file.Position, file.Length, 0, 1, name)), cancellationToken, Path.GetFileName(archivePath), encrypted: false);
        return new ExtractionResult(1, 0);
    }
    #endregion

    /// <summary>Copies <paramref name="input"/> to <paramref name="path"/>; a half-written file is removed on failure.</summary>
    private static void WriteFile(string path, Stream input, DateTime modified, Action<long> onBytes, CancellationToken cancellationToken, string archiveName, bool encrypted)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using (input)
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
            {
                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    written += read;
                    onBytes(written);
                }
            }

            TrySetTime(path, modified);
        }
        catch (Exception exception)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }

            // A wrong ZipCrypto password is only noticed once the data fails its check.
            if (encrypted && exception is ZipException)
            {
                throw new InvalidOperationException($"The password does not open {archiveName}.", exception);
            }

            throw;
        }
    }

    private static void TrySetTime(string path, DateTime modified)
    {
        try
        {
            File.SetLastWriteTime(path, modified);
        }
        catch
        {
            // A date out of range for the file system is not worth failing the extraction over.
        }
    }

    #region 7-Zip
    /// <summary>The switches for a run, password excluded (it goes on stdin).</summary>
    internal static List<string> BuildArguments(string archivePath, string targetFolder, ExtractionOptions options) =>
    [
        "x",
        "-bsp1",
        "-bb0",
        "-bse1",
        "-y",
        options.Overwrite ? "-aoa" : "-aos",   // overwrite all / skip existing
        "-o" + targetFolder,
        // No -p at all: when extracting, a bare -p means "the password is empty" (when adding it
        // means "ask"). Left out, 7-Zip asks only if the archive is encrypted, and reads the answer
        // from stdin — the password when there is one, the closed stream when there is not.
        "--",
        archivePath,
    ];

    private async Task<ExtractionResult> ExtractWithSevenZipAsync(
        string archivePath, string targetFolder, ExtractionOptions options, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        var executable = SevenZipExecutable ?? throw new InvalidOperationException("This archive needs 7-Zip installed.");
        var before = CountFiles(targetFolder);
        var output = new SevenZipArchiveWriter.PercentageStream(percent =>
            progress?.Report(new ArchiveProgress(percent, 100, 0, 0, string.Empty)));

        var result = await runner.RunToStreamAsync(
            executable,
            BuildArguments(Path.GetFullPath(archivePath), Path.GetFullPath(targetFolder), options),
            output,
            targetFolder,
            async stdin =>
            {
                if (options.HasPassword)
                {
                    var line = options.Password + Environment.NewLine;
                    await stdin.WriteAsync(Encoding.UTF8.GetBytes(line + line), cancellationToken);
                    await stdin.FlushAsync(cancellationToken);
                }
            },
            cancellationToken);

        if (!result.Success)
        {
            throw new IOException(DescribeFailure(result, output.Text, Path.GetFileName(archivePath), options.HasPassword));
        }

        // A .tar.xz is two layers, and 7-Zip takes off one: what it leaves is a .tar, unpacked here.
        var extracted = Math.Max(0, CountFiles(targetFolder) - before);
        if (archivePath.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase) || archivePath.EndsWith(".txz", StringComparison.OrdinalIgnoreCase))
        {
            var tar = Path.Combine(targetFolder, Path.GetFileNameWithoutExtension(archivePath) is var stem && stem.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ? stem : stem + ".tar");
            if (File.Exists(tar))
            {
                var inner = ExtractTar(tar, ".tar", targetFolder, options, progress, cancellationToken);
                File.Delete(tar);
                return inner;
            }
        }

        return new ExtractionResult(extracted, 0);
    }

    /// <summary>
    /// 7-Zip's failure in the user's words. Without a password, an encrypted archive is reported
    /// both as "Enter password" (asked, and met the closed stdin) and as "Wrong password?" — so
    /// which of the two messages is right depends on whether one was given, not on the text.
    /// </summary>
    internal static string DescribeFailure(ProcessOutput result, string output, string archiveName, bool hadPassword)
    {
        var text = output + "\n" + result.StandardError;
        var aboutPassword = text.Contains("password", StringComparison.OrdinalIgnoreCase);
        if (aboutPassword && !hadPassword)
        {
            return $"{archiveName} is encrypted: type its password.";
        }

        if (aboutPassword && text.Contains("Wrong password", StringComparison.OrdinalIgnoreCase))
        {
            return $"The password does not open {archiveName}.";
        }

        return SevenZipArchiveWriter.DescribeFailure(result, output);
    }

    private static int CountFiles(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count() : 0;
        }
        catch
        {
            return 0;
        }
    }
    #endregion

    /// <summary>Lets a reader be disposed without closing the file under it.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
