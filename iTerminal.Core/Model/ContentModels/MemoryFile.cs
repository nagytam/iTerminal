using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Model.ContentModels;

public class MemoryFile : MemoryBase, IMemoryFile
{
    public string FileName { get; set; }
    public byte[]? Content { get; set; }

    public MemoryFile(string fileName, MemoryStream inputStream) 
        : base(Container.MEMORY_DRIVE + fileName)
    {
        FileName = fileName;
        Content = inputStream.ToArray();
    }

    public MemoryFile(string fileName, byte[]? inputArray) 
        : base(Container.MEMORY_DRIVE + fileName)
    {
        FileName = fileName;
        Content = inputArray;
    }

    public static bool Rename(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        return false;
    }
}
