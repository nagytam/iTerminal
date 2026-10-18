using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using System.Diagnostics;

namespace iTerminal.Core.Model.ContentModels;

[DebuggerDisplay("{FullName}")]
public record MemoryDriveEntry : EntryBase, IEntry
{
    public MemoryDriveEntry(MemoryFile memoryFile)
    {
        Url = memoryFile.Url;
        Date = memoryFile.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
        FileSize = FileSizeHelper.ToFileSize(memoryFile.Content?.Length ?? 0);
    }

    public MemoryDriveEntry(MemoryFolder memoryFolder)
    {
        Url = memoryFolder.Url.FullUrl.EndsWith('/') ? memoryFolder.Url : new Url(memoryFolder.Url.FullUrl + "/");
        Date = memoryFolder.CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
        FileSize = "Folder";
    }

    public MemoryDriveEntry(string name)
    {
        Url = name;
    }

    public MemoryDriveEntry(string name, Url url)
    {
        Url = new Url(url, name);
    }
}