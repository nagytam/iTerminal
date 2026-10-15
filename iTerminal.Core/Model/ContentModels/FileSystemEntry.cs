using iConsole.Data;
using iConsole.Interfaces;
using System.Diagnostics;

namespace iTerminal.Core.Model.ContentModels;

[DebuggerDisplay("{FullName}")]
public record FileSystemEntry : EntryBase, IEntry
{
    public FileSystemEntry(string url)
    {
        Url = new Url(url);
        fileSize = Url.FileSize;
        date = Url.Date;
        time = Url.Time;
    }

    public FileSystemEntry(string name, string url)
    {
        Url = new Url(url, name);
        fileSize = Url.FileSize;
        date = Url.Date;
        time = Url.Time;
    }

    public FileSystemEntry(Url url)
    {
        Url = url;
        fileSize = Url.FileSize;
        date = Url.Date;
        time = Url.Time;
    }

    /// <summary>
    /// Fast constructor that creates a <see cref="FileSystemEntry"/> from a
    /// <see cref="FileSystemInfo"/> returned by directory enumeration.
    /// Avoids the extra <c>FileInfo</c> allocation and <c>Directory.Exists</c>
    /// syscall that the string-path constructor would trigger.
    /// </summary>
    public FileSystemEntry(FileSystemInfo info)
    {
        Url = new Url(info);
        fileSize = Url.FileSize;
        date = Url.Date;
        time = Url.Time;
    }
}