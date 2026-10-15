namespace iTerminal.Core.Data;

public class FolderContentsCacheEntry
{
    public string Path { get; set; } = string.Empty;

    public DateTimeOffset SavedAtUtc { get; set; }

    // JSON serialized list of entries (lightweight DTO)
    public string ContentJson { get; set; } = string.Empty;
}
