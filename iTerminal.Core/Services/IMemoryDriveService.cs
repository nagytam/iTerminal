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
}