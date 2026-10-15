using iConsole.Data;
using iConsole.Interfaces;
using System.Diagnostics;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A row on one of the drives built from a tool's output (docker://, git://, search://). Its size,
/// date and time come from that output rather than from <see cref="Url"/>, which cannot know them
/// for a path that is not on the local disk.
///
/// <para>The size column carries a word for a row that is not a plain file or folder ("Branch",
/// "Up", "Exited"), and the information strip shows the same word: the generic folder line says
/// "Files: …" until a recursive count lands, and no count is ever run off the local disk, so on
/// these drives it would say that forever.</para>
/// </summary>
[DebuggerDisplay("{FullName}")]
public record VirtualDriveEntry : EntryBase, IEntry
{
    public VirtualDriveEntry(Url url, string? displayName = null, string fileSize = "", DateTime? modified = null)
    {
        Url = displayName is null ? url : new Url(url.FullUrl, displayName);
        this.fileSize = fileSize;
        date = modified?.ToString("yyyy-MM-dd") ?? string.Empty;
        time = modified?.ToString("HH:mm") ?? string.Empty;
        Modified = modified;
    }

    public DateTime? Modified { get; }

    /// <summary>Size in bytes, when the source knows it; 0 otherwise.</summary>
    public long Size { get; init; }

    /// <summary>Optional second line for the information strip (a commit subject, a container image).</summary>
    public string Detail { get; init; } = string.Empty;

    public override string GetFileSize(int width) =>
        (Detail.Length > 0 ? Detail : FileSize).PadRight(width);

    public override string GetDisplayFileSize(int width) =>
        Url.PathType == PathType.File && Detail.Length == 0
            ? $"File size: {FileSize}".PadRight(width)
            : GetFileSize(width);
}
