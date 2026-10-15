namespace iTerminal.Core.Data.Music;

/// <summary>
/// Which stage of a music-library scan is running. The two working stages are separate because they
/// answer different questions: discovery can only say how many files it has seen so far, indexing
/// can say how far through a known total it is.
/// </summary>
public enum MusicIndexingPhase
{
    /// <summary>No scan is running.</summary>
    Idle,

    /// <summary>Walking the music folder to find out what there is to index.</summary>
    Discovering,

    /// <summary>Reading tags for the files discovery found. The total no longer moves.</summary>
    Indexing,
}

/// <summary>
/// An immutable snapshot of how far a scan has got, published by
/// <see cref="Services.Music.IMusicLibraryService.IndexingProgressChanged"/>.
///
/// <para>The whole point of splitting discovery from indexing is that "indexed 120 files" means
/// nothing on its own — a library can hold ten files or fifty thousand. Only once discovery is
/// complete is <see cref="DiscoveredFiles"/> a real total, and that is exactly what
/// <see cref="IsDiscoveryComplete"/> tells the UI.</para>
/// </summary>
/// <param name="Phase">The stage this snapshot was taken in.</param>
/// <param name="DiscoveredFiles">
/// <em>Audio</em> files found so far — the final total once discovery is done. Never the number of
/// files in the folder: a music folder is routinely mostly artwork caches and databases, and a
/// denominator that counted those could never be reached.
/// </param>
/// <param name="IndexedFiles">Files processed by the indexing stage so far.</param>
public sealed record MusicIndexingProgress(
    MusicIndexingPhase Phase,
    int DiscoveredFiles,
    int IndexedFiles)
{
    /// <summary>The "nothing is running" snapshot, which is also where every scan ends up.</summary>
    public static readonly MusicIndexingProgress Idle = new(MusicIndexingPhase.Idle, 0, 0);

    /// <summary>True while a scan is in either of its working stages.</summary>
    public bool IsRunning => Phase != MusicIndexingPhase.Idle;

    /// <summary>
    /// True once the folder walk has finished, which is the point at which
    /// <see cref="DiscoveredFiles"/> stops growing and a determinate "n of m" becomes meaningful.
    /// </summary>
    public bool IsDiscoveryComplete => Phase == MusicIndexingPhase.Indexing;

    /// <summary>Completed fraction 0..1, or null while the total is still unknown.</summary>
    public double? Fraction => IsDiscoveryComplete && DiscoveredFiles > 0
        ? Math.Clamp((double)IndexedFiles / DiscoveredFiles, 0d, 1d)
        : null;

    /// <summary>Completed percentage, or null while the total is still unknown.</summary>
    public int? Percent => Fraction is { } fraction ? (int)(fraction * 100) : null;

    /// <summary>
    /// One line for the panel footer. Empty when idle — there is nothing to report between scans.
    /// </summary>
    public string Describe() => Phase switch
    {
        MusicIndexingPhase.Discovering => $"discovering… {DiscoveredFiles} audio files found",
        MusicIndexingPhase.Indexing =>
            $"{DiscoveredFiles} audio files discovered, indexing {IndexedFiles}/{DiscoveredFiles}{PercentSuffix}",
        _ => string.Empty,
    };

    /// <summary>
    /// Short form for the panel title, where the count of what discovery found is already implied by
    /// the denominator and repeating it would only cost width.
    /// </summary>
    public string DescribeShort() => Phase switch
    {
        MusicIndexingPhase.Discovering => $"Discovering… {DiscoveredFiles} audio files",
        MusicIndexingPhase.Indexing => $"Indexing {IndexedFiles}/{DiscoveredFiles}{PercentSuffix}",
        _ => string.Empty,
    };

    private string PercentSuffix => Percent is { } percent ? $" ({percent}%)" : string.Empty;
}
