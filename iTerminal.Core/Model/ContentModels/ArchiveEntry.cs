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
    public override string DisplayName => ZipArchiveEntry?.Name ?? _label ?? Url.Name;
    public override string Name => ZipArchiveEntry?.Name ?? _label ?? Url.Name;
    public override string DirectoryName => Url.Directory;
    public ZipArchiveEntry? ZipArchiveEntry { get; set; }
    public override bool Exists => ZipArchiveEntry != null;

    /// <summary>A row that is not a zip entry of its own: the ".." row, or a folder implied by the entries under it.</summary>
    /// <remarks>The label is what the row shows and what <see cref="FocusedEntry.IsParentEntry"/> matches, so it must be given: the
    /// URL's own name is the folder's, which is why ".." used to show as "tmp" and was not recognised as the parent row.</remarks>
    public ArchiveEntry(string displayName, string url)
    {
        _label = displayName;
        Url = new Url(url, displayName);
    }

    private readonly string? _label;

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