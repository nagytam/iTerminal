namespace iTerminal.Core.Services;

/// <summary>The <see cref="IDirectoryLister"/> for the local disk.</summary>
public sealed class DirectoryLister : IDirectoryLister
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        // Hidden entries are offered (a dot-folder is a fair thing to type); system ones ($RECYCLE.BIN, System Volume Information) are not.
        AttributesToSkip = FileAttributes.System,
        ReturnSpecialDirectories = false,
    };

    /// <inheritdoc/>
    public IReadOnlyList<DirectoryChild> List(string folder, int max)
    {
        List<DirectoryChild> children = [];
        if (string.IsNullOrWhiteSpace(folder) || max <= 0)
        {
            return children;
        }

        try
        {
            foreach (FileSystemInfo entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Options))
            {
                children.Add(new DirectoryChild(entry.Name, entry is DirectoryInfo));
                if (children.Count >= max)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Not there, not readable, or not a path: nothing to offer.
        }

        return children;
    }
}
