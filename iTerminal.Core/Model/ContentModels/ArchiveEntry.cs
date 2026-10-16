using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using System.Diagnostics;
using System.IO.Compression;

namespace iTerminal.Core.Model.ContentModels;

[DebuggerDisplay("{FullName}")]
public record ArchiveEntry : EntryBase, IEntry
{
    public override string FileSize => ZipArchiveEntry?.Length.ToFileSize() ?? string.Empty;
    public override string Date => ZipArchiveEntry?.LastWriteTime.ToString("yyyy-MM-dd") ?? string.Empty;
    public override string Time => ZipArchiveEntry?.LastWriteTime.ToString("HH:mm:ss") ?? string.Empty;
    public override string FullName => ZipArchiveEntry?.FullName ?? Url.FullName;
    public override string DisplayName => ZipArchiveEntry?.Name ?? Url.Name;
    public override string Name => ZipArchiveEntry?.Name ?? Url.Name;
    public override string DirectoryName => Url.Directory;
    public ZipArchiveEntry? ZipArchiveEntry { get; set; }
    public override bool Exists => ZipArchiveEntry != null;

    public ArchiveEntry(string displayName, string url)
    {
        Url = new Url(url, displayName);
    }

    public ArchiveEntry(ZipArchiveEntry zipArchiveEntry)
    {
        ZipArchiveEntry = zipArchiveEntry;
    }

    public override string GetName(int width)
    {
        return ZipArchiveEntry?.Name.PadRight(width) ?? string.Empty;
    }

    public override string GetFileSize(int width)
    {
        return ZipArchiveEntry?.Length.ToFileSize().PadRight(width) ?? string.Empty;
    }
}