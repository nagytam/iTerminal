namespace iConsole.Hdf;

/// <summary>One dataset in an HDF5 file.</summary>
/// <param name="Path">The absolute path inside the file, starting with '/'.</param>
/// <param name="Shape">The size of each dimension; empty for a scalar.</param>
/// <param name="TypeName">A short name for the element type, such as <c>float64</c> or <c>compound (2 fields)</c>.</param>
public sealed record HdfDatasetInfo(string Path, long[] Shape, string TypeName)
{
    public int Rank => Shape.Length;

    /// <summary>How many leading dimensions a slice fixes by index: all but the last two.</summary>
    public int LeadingDimensions => Math.Max(0, Shape.Length - 2);

    public override string ToString() => Path;
}

/// <summary>The datasets of a file, and whether the cap cut the list short.</summary>
public sealed record HdfDatasetList(IReadOnlyList<HdfDatasetInfo> Datasets, bool Truncated);

/// <summary>One 2-D view of a dataset: column names and cell text, and which axes the caps cut short.</summary>
public sealed record HdfSlice(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, bool RowsTruncated, bool ColumnsTruncated);

/// <summary>A file or dataset the reader refused or could not read; the message is meant for the status line.</summary>
public sealed class HdfReadException : Exception
{
    public HdfReadException(string message) : base(message)
    {
    }

    public HdfReadException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Reads an HDF5 file for the HDF5 viewer, read-only. The implementation lives in iTerminal.Core, so the UI has no HDF
/// dependency. Both calls block on file I/O: callers run them on a worker thread.
/// </summary>
public interface IHdfFileReader
{
    /// <summary>The datasets of the file in path order, at most <paramref name="maxDatasets"/>. Throws <see cref="HdfReadException"/> when the file is not HDF5.</summary>
    Task<HdfDatasetList> ListDatasetsAsync(string path, int maxDatasets, CancellationToken cancellationToken);

    /// <summary>
    /// A 2-D view of a dataset. A dataset of three or more dimensions is cut at <paramref name="leadingIndices"/>, one index for
    /// each dimension but the last two; the last two become the rows and the columns. Only the cells shown are read.
    /// </summary>
    /// <param name="leadingIndices">One index per leading dimension; ignored for a dataset of two dimensions or fewer.</param>
    /// <param name="maxRows">The most rows kept.</param>
    /// <param name="maxColumns">The most columns kept.</param>
    /// <param name="maxCells">The most cells kept; the rows are cut first to honour it.</param>
    Task<HdfSlice> ReadSliceAsync(
        string path,
        string datasetPath,
        long[] leadingIndices,
        int maxRows,
        int maxColumns,
        int maxCells,
        CancellationToken cancellationToken);
}
