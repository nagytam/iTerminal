using iConsole.Tabular;

namespace iConsole.Hdf;

/// <summary>One dataset of an HDF5 file as a grid array: its shape, its compound fields, and its blocks read through the reader.</summary>
public sealed class HdfGridArray(IHdfFileReader reader, string path, HdfDatasetInfo dataset) : IGridArraySource
{
    public IReadOnlyList<long> Shape { get; } = dataset.Shape;

    public IReadOnlyList<string> ColumnNames { get; } = [];

    public IReadOnlyList<string> Fields { get; } = dataset.Fields ?? [];

    public Task<IReadOnlyList<string>> ReadBlockAsync(long[] start, long[] count, CancellationToken cancellationToken) =>
        reader.ReadBlockAsync(path, dataset.Path, start, count, cancellationToken);
}
