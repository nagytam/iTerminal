namespace iConsole.Har;

/// <summary>
/// A HAR file (HTTP Archive 1.2, the network log a browser's developer tools export): the entries of its <c>log</c>, in order.
/// It is read in one pass; a file past <see cref="MaxEntries"/> keeps its first entries and says so.
/// </summary>
public sealed class HarDocument
{
    /// <summary>The most entries the viewer lists. A longer log is cut here, and <see cref="Truncated"/> says so.</summary>
    public const int MaxEntries = 100_000;

    /// <summary>Builds the document from entries already read; the parser in <c>iConsole.Formats</c> does the reading.</summary>
    public HarDocument(IReadOnlyList<HarEntry> entries, bool truncated)
    {
        Entries = entries;
        Truncated = truncated;
    }

    public IReadOnlyList<HarEntry> Entries { get; }

    /// <summary>True when the file had more entries than <see cref="MaxEntries"/>.</summary>
    public bool Truncated { get; }
}
