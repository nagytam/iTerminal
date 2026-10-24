namespace iTerminal.Core.Data;

/// <summary>A file's or folder's colour label and tags, keyed by its full path. Only what has a label has a row.</summary>
public class FileLabelEntry
{
    public string Path { get; set; } = string.Empty;

    public string Colour { get; set; } = string.Empty;

    /// <summary>The tags, comma-separated; a tag cannot contain a comma.</summary>
    public string Tags { get; set; } = string.Empty;
}
