namespace iTerminal.Core.Data.Documents;

/// <summary>
/// Which stage of a document scan is running. Split for the same reason the music scan is —
/// discovery can only report a rising count, indexing knows its denominator — but the split earns
/// far more here: extracting a PDF or OCR'ing a scan is seconds per file, so a scan that only said
/// "working…" would look hung for minutes on a real documents folder.
/// </summary>
public enum DocumentIndexingPhase
{
    /// <summary>No scan is running.</summary>
    Idle,

    /// <summary>Walking the documents folder to find out what there is to index.</summary>
    Discovering,

    /// <summary>Extracting text from the files discovery found. The total no longer moves.</summary>
    Indexing,
}

/// <summary>
/// An immutable snapshot of how far a document scan has got, published by
/// <see cref="Services.Documents.IDocumentLibraryService.IndexingProgressChanged"/>.
/// </summary>
/// <param name="Phase">The stage this snapshot was taken in.</param>
/// <param name="DiscoveredFiles">
/// Files found so far — the final total once discovery is done. Unlike the music scan this counts
/// every regular file, not only the ones an extractor claims: a documents folder is a folder of
/// documents, and hiding the unsupported ones from the denominator would make the count disagree
/// with what the folder visibly holds.
/// </param>
/// <param name="IndexedFiles">Files processed by the indexing stage so far.</param>
/// <param name="CurrentFile">
/// The file being extracted right now, for the one thing the counts cannot show: which document is
/// taking the time. Empty outside the indexing stage.
/// </param>
public sealed record DocumentIndexingProgress(
    DocumentIndexingPhase Phase,
    int DiscoveredFiles,
    int IndexedFiles,
    string CurrentFile = "")
{
    /// <summary>The "nothing is running" snapshot, which is also where every scan ends up.</summary>
    public static readonly DocumentIndexingProgress Idle = new(DocumentIndexingPhase.Idle, 0, 0);

    /// <summary>True while a scan is in either of its working stages.</summary>
    public bool IsRunning => Phase != DocumentIndexingPhase.Idle;

    /// <summary>
    /// True once the folder walk has finished, which is the point at which
    /// <see cref="DiscoveredFiles"/> stops growing and a determinate "n of m" becomes meaningful.
    /// </summary>
    public bool IsDiscoveryComplete => Phase == DocumentIndexingPhase.Indexing;

    /// <summary>Completed fraction 0..1, or null while the total is still unknown.</summary>
    public double? Fraction => IsDiscoveryComplete && DiscoveredFiles > 0
        ? Math.Clamp((double)IndexedFiles / DiscoveredFiles, 0d, 1d)
        : null;

    /// <summary>Completed percentage, or null while the total is still unknown.</summary>
    public int? Percent => Fraction is { } fraction ? (int)(fraction * 100) : null;

    /// <summary>One line for the dialog footer. Empty when idle — nothing to report between scans.</summary>
    public string Describe() => Phase switch
    {
        DocumentIndexingPhase.Discovering => $"Discovering… {DiscoveredFiles} files found",
        DocumentIndexingPhase.Indexing => string.IsNullOrEmpty(CurrentFile)
            ? $"Indexing {IndexedFiles}/{DiscoveredFiles}{PercentSuffix}"
            : $"Indexing {IndexedFiles}/{DiscoveredFiles}{PercentSuffix} — {CurrentFile}",
        _ => string.Empty,
    };

    /// <summary>
    /// Short form for the dialog title, where the file name would cost the width the counts need.
    /// </summary>
    public string DescribeShort() => Phase switch
    {
        DocumentIndexingPhase.Discovering => $"Discovering… {DiscoveredFiles}",
        DocumentIndexingPhase.Indexing => $"Indexing {IndexedFiles}/{DiscoveredFiles}{PercentSuffix}",
        _ => string.Empty,
    };

    private string PercentSuffix => Percent is { } percent ? $" ({percent}%)" : string.Empty;
}
