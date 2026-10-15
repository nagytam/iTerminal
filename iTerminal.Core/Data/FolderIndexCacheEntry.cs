namespace iTerminal.Core.Data;

public class FolderIndexCacheEntry
{
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset ModifiedUtc { get; set; }
}
