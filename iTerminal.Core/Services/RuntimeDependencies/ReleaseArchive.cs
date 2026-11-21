using System.Formats.Tar;
using System.IO.Compression;
using iTerminal.Core.Helper;
using ZstdSharp;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// Unpacks the archives a GitHub release carries (zip, tar.gz, tar.zst) for the runtime dependencies that
/// install a program into the data folder.
/// </summary>
internal static class ReleaseArchive
{
    /// <summary>
    /// Unpacks a release into <paramref name="destination"/>. Every entry must land inside it (the "zip slip");
    /// one that would not is skipped. Symbolic links are kept when they stay inside as well, because the
    /// program's GPU libraries are linked that way.
    /// </summary>
    internal static void Extract(string package, string destination)
    {
        Directory.CreateDirectory(destination);
        string destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        if (package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using ZipArchive archive = ZipFile.OpenRead(package);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                string? target = ArchivePaths.Inside(destination,entry.FullName);
                if (target is null)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            return;
        }

        using Stream file = File.OpenRead(package);
        using Stream archiveStream = package.EndsWith(".tar.zst", StringComparison.OrdinalIgnoreCase)
            ? new DecompressionStream(file)
            : new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(archiveStream);
        while (tar.GetNextEntry() is { } tarEntry)
        {
            string? target = ArchivePaths.Inside(destination,tarEntry.Name);
            if (target is null)
            {
                continue;
            }

            switch (tarEntry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;

                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    tarEntry.ExtractToFile(target, overwrite: true);
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(target, tarEntry.Mode);
                    }

                    break;

                case TarEntryType.SymbolicLink when !OperatingSystem.IsWindows():
                    // Only a link whose target also stays inside the destination.
                    string linkTarget = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(target)!, tarEntry.LinkName));
                    if (!linkTarget.StartsWith(destinationRoot, StringComparison.Ordinal))
                    {
                        break;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    TryDeleteFile(target);
                    File.CreateSymbolicLink(target, tarEntry.LinkName);
                    break;

                default:
                    // Hard links and devices have no place in a release of this program.
                    break;
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
