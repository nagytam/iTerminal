using iConsole.Data;

namespace iConsole.Interfaces;

/// <summary>
/// An <see cref="IFileSystem"/> that can say how many bytes are free where a given entry would be
/// written. Optional: drives that cannot tell (remote, archive, virtual ones) simply do not
/// implement it, and a transfer to them is never held back by a space check.
/// </summary>
public interface IFreeSpaceFileSystem
{
    /// <summary>Free bytes on the volume holding <paramref name="url"/>, or null when unknown.</summary>
    long? GetFreeBytes(Url url);

    /// <summary>
    /// Identifies the volume holding <paramref name="url"/>, or null when unknown. Two entries on
    /// the same volume can be moved by renaming, which needs no free space.
    /// </summary>
    string? GetVolumeId(Url url);
}
