namespace iConsole.Parquet;

/// <summary>The first rows of a Parquet file as text, with the schema in the headers.</summary>
/// <param name="Headers">One header per shown column: the name and the type, such as <c>id (int64)</c>.</param>
/// <param name="Rows">The shown rows, each with one cell per shown column. A null cell is an empty string.</param>
/// <param name="TotalRows">The number of rows in the file, from its footer.</param>
/// <param name="TotalColumns">The number of top-level columns in the file.</param>
/// <param name="ColumnNames">The bare column names, one per shown column, for a query to name; null means the headers are the names.</param>
public sealed record ParquetPreview(IReadOnlyList<string> Headers, IReadOnlyList<string[]> Rows, long TotalRows, int TotalColumns, IReadOnlyList<string>? ColumnNames = null)
{
    /// <summary>The names a query matches: the bare column names, without the type.</summary>
    public IReadOnlyList<string> Names => ColumnNames ?? Headers;

    public bool RowsTruncated => TotalRows > Rows.Count;

    public bool ColumnsTruncated => TotalColumns > Headers.Count;
}

/// <summary>A file the reader refused or could not read; the message is meant for the status line.</summary>
public sealed class ParquetReadException : Exception
{
    public ParquetReadException(string message) : base(message)
    {
    }

    public ParquetReadException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Reads a Parquet file for the Parquet viewer, read-only. The implementation lives in iTerminal.Core, so the UI has no Parquet
/// dependency. The call blocks on file I/O: callers run it on a worker thread.
/// </summary>
public interface IParquetFileReader
{
    /// <summary>
    /// The first rows of the file, at most <paramref name="maxRows"/> rows and <paramref name="maxColumns"/> top-level columns, and
    /// at most <paramref name="maxCells"/> cells (the rows are cut first to honour it). Only the row groups shown are read.
    /// Throws <see cref="ParquetReadException"/> when the file is not Parquet.
    /// </summary>
    Task<ParquetPreview> ReadPreviewAsync(string path, int maxRows, int maxColumns, int maxCells, CancellationToken cancellationToken);
}
