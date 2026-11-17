using iTerminal.Core.Helper;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Services.Archives;

/// <summary>
/// XZ and TAR.XZ, compressed by the 7-Zip command-line tool (<c>7z a -txz</c>): no managed library
/// writes XZ. A single file is staged to a temporary file first; a TAR.XZ is built in-process as a
/// temporary .tar first. 7-Zip then compresses that file to a fresh temporary name beside the target,
/// which is moved over the target only on success. No update switches are used, so nothing is merged
/// into an existing archive.
/// </summary>
internal sealed class SevenZipStreamWriter(IExternalProcessRunner runner, SevenZipArchiveWriter sevenZip)
{
    public async Task<CompressionResult> WriteAsync(
        string archivePath,
        IReadOnlyList<ArchiveItem> items,
        CompressionOptions options,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var executable = sevenZip.Executable ?? throw new InvalidOperationException($"{options.Format.Name} needs 7-Zip installed.");
        var folder = Path.GetDirectoryName(Path.GetFullPath(archivePath))!;

        // Beside the target, not in the system temp folder: the staged copy is as large as the input, and the target's drive has room for it.
        var work = Path.Combine(folder, "." + Path.GetFileName(archivePath) + "." + Guid.NewGuid().ToString("N")[..8] + ".work");
        var output = Path.Combine(folder, $".{Path.GetFileName(archivePath)}.{Guid.NewGuid():N}.xz");
        var files = items.Count(i => !i.IsFolder);
        var total = items.Where(i => !i.IsFolder).Sum(i => Math.Max(0, i.Size));
        Directory.CreateDirectory(work);
        try
        {
            string input;
            if (options.Format == ArchiveFormat.TarXz)
            {
                input = Path.Combine(work, "archive.tar");
                await ManagedArchiveWriter.WriteAsync(input, items, new CompressionOptions(ArchiveFormat.Tar, 0), progress, cancellationToken);
            }
            else
            {
                input = Path.Combine(work, "input");
                var item = items.Single(i => !i.IsFolder);
                await using var file = new FileStream(input, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                var staged = new SynchronousProgress<long>(bytes =>
                    progress?.Report(new ArchiveProgress(bytes, total, 0, files, Path.GetFileName(archivePath))));
                await item.WriteToAsync(new ProgressStream(file, staged), cancellationToken);
            }

            var stdout = new SevenZipArchiveWriter.PercentageStream(percent =>
                progress?.Report(new ArchiveProgress(total * percent / 100, total, files * percent / 100, files, Path.GetFileName(archivePath))));
            var arguments = new List<string>
            {
                "a",
                "-txz",
                $"-mx={Math.Clamp(options.Level, 1, 9)}",
                "-bsp1",
                "-bb0",
                "-bse1",
                "-y",
                "--",
                output,
                input,
            };

            // Stdin is closed empty: XZ takes no password, so 7-Zip has nothing to ask for.
            var result = await runner.RunToStreamAsync(executable, arguments, stdout, work, _ => Task.CompletedTask, cancellationToken);
            if (!result.Success)
            {
                throw new IOException(SevenZipArchiveWriter.DescribeFailure(result, stdout.Text));
            }

            File.Move(output, archivePath, overwrite: true);
            return new CompressionResult(files, 0, 0);
        }
        finally
        {
            try
            {
                if (File.Exists(output))
                {
                    File.Delete(output);
                }

                if (Directory.Exists(work))
                {
                    Directory.Delete(work, recursive: true);
                }
            }
            catch
            {
                // Litter in the temp folder, not a failure of the compression.
            }
        }
    }
}
