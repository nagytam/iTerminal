using System.Collections;
using System.Globalization;
using iConsole.Hdf;
using PureHDF;
using PureHDF.Selections;

namespace iTerminal.Core.Services.Hdf;

/// <summary>
/// Reads an HDF5 file for the HDF5 viewer, with PureHDF (managed, no native library). The file is opened read-only, once per
/// call, and closed when the call ends. A slice is read as a hyperslab, so only the cells shown are decoded, never the whole
/// dataset; PureHDF cannot cancel a read already running, so cancellation is checked between steps and a slice is bounded by
/// the caps the caller passes.
///
/// <para>Values read as text: integers as they are, floats as the shortest text that reads back the same (<c>NaN</c> and
/// <c>Infinity</c> as words), strings as text, an enumeration or a bit field as its stored integer. A compound element
/// reads as <c>{a=1, b=2}</c>, except in a one-dimensional dataset, where each field is a column. A type the viewer does not
/// decode (a reference, an opaque value, an array or a time) shows <c>&lt;unsupported type&gt;</c> in every cell.</para>
/// </summary>
public sealed class Hdf5FileReader : IHdfFileReader
{
    /// <summary>What a cell of a type the viewer does not decode shows.</summary>
    public const string UnsupportedCell = "<unsupported type>";

    private const int MaxDepth = 32;
    private const int MaxGroups = 20_000;

    public Task<HdfDatasetList> ListDatasetsAsync(string path, int maxDatasets, CancellationToken cancellationToken)
    {
        try
        {
            CheckFile(path);
            using var file = H5File.OpenRead(path);
            var datasets = new List<HdfDatasetInfo>();
            int groups = 0;
            bool truncated = false;
            Collect(file, string.Empty, 0, datasets, maxDatasets, ref groups, ref truncated, cancellationToken);
            return Task.FromResult(new HdfDatasetList(datasets, truncated));
        }
        catch (Exception ex) when (ex is not HdfReadException and not OperationCanceledException)
        {
            throw new HdfReadException($"The file could not be read as HDF5: {ex.Message}", ex);
        }
    }

    private static void Collect(
        IH5Group group,
        string prefix,
        int depth,
        List<HdfDatasetInfo> datasets,
        int maxDatasets,
        ref int groups,
        ref bool truncated,
        CancellationToken cancellationToken)
    {
        if (depth > MaxDepth || ++groups > MaxGroups)
        {
            return;
        }

        List<IH5Object> children;
        try
        {
            children = [.. group.Children().OrderBy(child => child.Name, StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A group whose links cannot be followed (a dangling soft link, say) is skipped, not fatal.
            return;
        }

        foreach (IH5Object child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = prefix + "/" + child.Name;
            if (child is IH5Dataset dataset)
            {
                if (datasets.Count >= maxDatasets)
                {
                    truncated = true;
                    return;
                }

                datasets.Add(Describe(fullPath, dataset));
            }
            else if (child is IH5Group subgroup)
            {
                Collect(subgroup, fullPath, depth + 1, datasets, maxDatasets, ref groups, ref truncated, cancellationToken);
                if (truncated)
                {
                    return;
                }
            }
        }
    }

    private static HdfDatasetInfo Describe(string path, IH5Dataset dataset)
    {
        long[] shape = dataset.Space.Type == H5DataspaceType.Null
            ? [0]
            : [.. dataset.Space.Dimensions.Select(dimension => (long)dimension)];
        return new HdfDatasetInfo(path, shape, TypeName(dataset.Type));
    }

    private static string TypeName(IH5DataType type) => type.Class switch
    {
        H5DataTypeClass.FixedPoint => $"{(type.FixedPoint.IsSigned ? "int" : "uint")}{type.Size * 8}",
        H5DataTypeClass.FloatingPoint => $"float{type.Size * 8}",
        H5DataTypeClass.String => "string",
        // A variable-length sequence of bytes is how HDF5 stores a variable-length string.
        H5DataTypeClass.VariableLength => type.VariableLength.BaseType is { Class: H5DataTypeClass.FixedPoint, Size: 1 } ? "string" : "variable-length",
        H5DataTypeClass.Compound => $"compound ({type.Compound.Members.Count()} fields)",
        H5DataTypeClass.Enumerated => "enum",
        H5DataTypeClass.Array => "array",
        H5DataTypeClass.BitField => "bitfield",
        H5DataTypeClass.Opaque => "opaque",
        H5DataTypeClass.Reference => "reference",
        H5DataTypeClass.Time => "time",
        _ => "unknown",
    };

    public Task<HdfSlice> ReadSliceAsync(
        string path,
        string datasetPath,
        long[] leadingIndices,
        int maxRows,
        int maxColumns,
        int maxCells,
        CancellationToken cancellationToken)
    {
        try
        {
            CheckFile(path);
            using var file = H5File.OpenRead(path);
            IH5Dataset dataset = Find(file, datasetPath);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Slice(dataset, leadingIndices, maxRows, maxColumns, maxCells, cancellationToken));
        }
        catch (Exception ex) when (ex is not HdfReadException and not OperationCanceledException)
        {
            throw new HdfReadException($"The dataset could not be read: {ex.Message}", ex);
        }
    }

    private static void CheckFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new HdfReadException("The file was not found");
        }

        if (!HdfFileTypes.FileHasSignature(path))
        {
            throw new HdfReadException("Not an HDF5 file");
        }
    }

    private static IH5Dataset Find(IH5Group root, string datasetPath)
    {
        try
        {
            return root.Dataset(datasetPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new HdfReadException($"The dataset {datasetPath} was not found", ex);
        }
    }

    private static HdfSlice Slice(
        IH5Dataset dataset,
        long[] leadingIndices,
        int maxRows,
        int maxColumns,
        int maxCells,
        CancellationToken cancellationToken)
    {
        int rank = dataset.Space.Rank;
        long[] dimensions = [.. dataset.Space.Dimensions.Select(dimension => (long)dimension)];
        if (dataset.Space.Type == H5DataspaceType.Null || dimensions.Any(dimension => dimension == 0))
        {
            return new HdfSlice([], [], false, false);
        }

        if (rank == 0)
        {
            string[] scalar = ReadCells(dataset, null, cancellationToken);
            return new HdfSlice(["value"], [[scalar.Length > 0 ? scalar[0] : string.Empty]], false, false);
        }

        // One compound column per field in a one-dimensional dataset; everything else is a 2-D block.
        bool compoundColumns = rank == 1 && dataset.Type.Class == H5DataTypeClass.Compound;
        string[] fields = compoundColumns ? [.. dataset.Type.Compound.Members.Select(member => member.Name)] : [];
        long rowsAvailable = rank == 1 ? dimensions[0] : dimensions[rank - 2];
        long columnsAvailable = rank == 1 ? (compoundColumns ? fields.Length : 1) : dimensions[rank - 1];
        int columns = (int)Math.Min(columnsAvailable, Math.Max(1, maxColumns));
        int rows = (int)Math.Min(rowsAvailable, Math.Min(Math.Max(1, maxRows), Math.Max(1, maxCells / columns)));

        // A rank-1 dataset reads `rows` elements; a dataset of rank 2 or more reads a rows x columns block at the leading indices.
        var starts = new ulong[rank];
        var blocks = new ulong[rank];
        for (int dimension = 0; dimension < rank - 2; dimension++)
        {
            long index = dimension < leadingIndices.Length ? leadingIndices[dimension] : 0;
            starts[dimension] = (ulong)Math.Clamp(index, 0, dimensions[dimension] - 1);
            blocks[dimension] = 1;
        }

        if (rank == 1)
        {
            blocks[0] = (ulong)rows;
        }
        else
        {
            blocks[rank - 2] = (ulong)rows;
            blocks[rank - 1] = (ulong)columns;
        }

        var selection = new HyperslabSelection(rank, starts, blocks);
        var table = new List<string[]>(rows);
        string[] headers;
        if (compoundColumns)
        {
            headers = ["#", .. fields.Take(columns)];
            Dictionary<string, object>[] elements = dataset.Read<Dictionary<string, object>[]>(fileSelection: selection);
            cancellationToken.ThrowIfCancellationRequested();
            for (int row = 0; row < elements.Length; row++)
            {
                table.Add([row.ToString(CultureInfo.InvariantCulture), .. fields.Take(columns).Select(field => elements[row].TryGetValue(field, out object? value) ? FormatObject(value) : string.Empty)]);
            }
        }
        else
        {
            string[] cells = ReadCells(dataset, selection, cancellationToken);
            headers = rank == 1
                ? ["#", "value"]
                : ["#", .. Enumerable.Range(0, columns).Select(column => column.ToString(CultureInfo.InvariantCulture))];
            int rowCount = Math.Min(rows, cells.Length / columns);
            for (int row = 0; row < rowCount; row++)
            {
                var line = new string[columns + 1];
                line[0] = row.ToString(CultureInfo.InvariantCulture);
                Array.Copy(cells, row * columns, line, 1, columns);
                table.Add(line);
            }
        }

        return new HdfSlice(headers, table, rows < rowsAvailable, columns < columnsAvailable);
    }

    /// <summary>The cells of a selection, flattened row by row, as text. Only the selected cells are decoded.</summary>
    private static string[] ReadCells(IH5Dataset dataset, Selection? selection, CancellationToken cancellationToken)
    {
        IH5DataType type = dataset.Type;
        string[] cells = type.Class switch
        {
            H5DataTypeClass.FixedPoint or H5DataTypeClass.BitField => ReadInteger(dataset, selection, type.Class == H5DataTypeClass.BitField || !type.FixedPoint.IsSigned, type.Size),
            H5DataTypeClass.FloatingPoint => ReadFloat(dataset, selection, type.Size),
            H5DataTypeClass.String => Read<string>(dataset, selection, text => text),
            H5DataTypeClass.VariableLength => ReadVariableLength(dataset, selection),
            H5DataTypeClass.Compound => dataset.Read<Dictionary<string, object>[]>(fileSelection: selection).Select(FormatCompound).ToArray(),
            H5DataTypeClass.Enumerated => ReadInteger(dataset, selection, unsigned: !type.Enumeration.BaseType.FixedPoint.IsSigned, type.Size),
            _ => Unsupported(selection),
        };
        cancellationToken.ThrowIfCancellationRequested();
        return cells;
    }

    private static string[] Read<T>(IH5Dataset dataset, Selection? selection, Func<T, string> format) =>
        [.. dataset.Read<T[]>(fileSelection: selection).Select(format)];

    private static string[] ReadInteger(IH5Dataset dataset, Selection? selection, bool unsigned, int size) => (unsigned, size) switch
    {
        (false, 1) => Read<sbyte>(dataset, selection, value => value.ToString(CultureInfo.InvariantCulture)),
        (false, 2) => Read<short>(dataset, selection, value => value.ToString(CultureInfo.InvariantCulture)),
        (false, 4) => Read<int>(dataset, selection, value => value.ToString(CultureInfo.InvariantCulture)),
        (false, 8) => Read<long>(dataset, selection, value => value.ToString(CultureInfo.InvariantCulture)),
        (true, 1) => Read<byte>(dataset, selection, value => value.ToString(CultureInfo.InvariantCulture)),
        (true, 2) => Read<ushort>(dataset, selection, value => value.ToString(CultureInfo.InvariantCulture)),
        (true, 4) => Read<uint>(dataset, selection, value => value.ToString(CultureInfo.InvariantCulture)),
        (true, 8) => Read<ulong>(dataset, selection, value => value.ToString(CultureInfo.InvariantCulture)),
        _ => Unsupported(selection),
    };

    private static string[] ReadFloat(IH5Dataset dataset, Selection? selection, int size) => size switch
    {
        2 => Read<Half>(dataset, selection, value => value.ToString("R", CultureInfo.InvariantCulture)),
        4 => Read<float>(dataset, selection, value => value.ToString("R", CultureInfo.InvariantCulture)),
        8 => Read<double>(dataset, selection, value => value.ToString("R", CultureInfo.InvariantCulture)),
        _ => Unsupported(selection),
    };

    /// <summary>Variable-length strings read as text; a variable-length sequence of anything else is not decoded.</summary>
    private static string[] ReadVariableLength(IH5Dataset dataset, Selection? selection)
    {
        try
        {
            return Read<string>(dataset, selection, text => text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Unsupported(selection);
        }
    }

    private static string[] Unsupported(Selection? selection)
    {
        long count = selection is null ? 1 : (long)selection.TotalElementCount;
        return [.. Enumerable.Repeat(UnsupportedCell, (int)Math.Min(count, int.MaxValue))];
    }

    private static string FormatCompound(Dictionary<string, object> element) =>
        "{" + string.Join(", ", element.Select(pair => $"{pair.Key}={FormatObject(pair.Value)}")) + "}";

    private static string FormatObject(object? value) => value switch
    {
        null => string.Empty,
        string text => text,
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        Dictionary<string, object> nested => FormatCompound(nested),
        IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(FormatObject)) + "]",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
