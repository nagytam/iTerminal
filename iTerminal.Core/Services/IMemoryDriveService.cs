using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Services;

public interface IMemoryDriveService : IFileSystem
{
    public MemoryFolder Root { get; }

    /// <summary>
    /// Resolves the folder addressed by <paramref name="url"/> (its <see cref="Url.Path"/>),
    /// walking down from <see cref="Root"/>. Returns <see langword="null"/> if any path segment
    /// is missing.
    /// </summary>
    MemoryFolder? GetFolder(Url url);

    /// <summary>
    /// Raised when files mirrored from an earlier run have been loaded into the drive, which can be
    /// after a panel has already listed it. The default does nothing.
    /// </summary>
    event EventHandler? MirrorLoaded
    {
        add { }
        remove { }
    }

    /// <summary>Raises <see cref="MirrorLoaded"/>.</summary>
    void NotifyMirrorLoaded()
    {
    }
}