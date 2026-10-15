using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services;

public interface IArchiveFileSystemService : IFileSystem
{
    /// <summary>
    /// Enumerates the direct children (files and folders) of the folder addressed by
    /// <paramref name="folderUrl"/> inside its archive.
    /// </summary>
    IEnumerable<Url> ListChildren(Url folderUrl);
}
