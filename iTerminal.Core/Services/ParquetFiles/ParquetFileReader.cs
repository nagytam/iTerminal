using System.Globalization;
using iConsole.Parquet;
using Parquet;
using Parquet.Schema;

namespace iTerminal.Core.Services.ParquetFiles;

/// <summary>
/// Reads the first rows of a Parquet file for the Parquet viewer, with Parquet.Net (managed, no native library). The file is
/// opened read-only, once per call, and closed when the call ends. Only the row groups that hold the rows shown are read, and
/// only the top-level columns shown.
///
/// <para>Values read as text: integers and floats as their invariant text, booleans as <c>True</c> or <c>False</c>, dates as
/// <c>yyyy-MM-dd</c> (with the time when there is one), strings as they are. A null cell is an empty string. A nested column
/// (a list, a struct or a map) shows <c>&lt;nested&gt;</c> in every row, and a type the viewer does not decode (binary, for one)
/// shows <c>&lt;unsupported type&gt;</c>; their values are not read.</para>
///
/// <para>The reader cannot cancel a row group already being decoded, so cancellation is checked between row groups and
/// columns.</para>
/// </summary>
public sealed class ParquetFileReader : IParquetFileReader
{
    /// <summary>What a cell of a nested column shows.</summary>
    public const string NestedCell = "<nested>";

    /// <summary>What a cell of a type the viewer does not decode shows.</summary>
    public const string UnsupportedCell = "<unsupported type>";

    public async Task<ParquetPreview> ReadPreviewAsync(string path, int maxRows, int maxColumns, int maxCells, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new ParquetReadException("The file was not found");
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
            await using ParquetReader reader = await ParquetReader.CreateAsync(stream, new ParquetOptions(), true, cancellationToken);
            IReadOnlyList<Field> fields = reader.Schema.Fields;
            int shownColumns = Math.Min(fields.Count, Math.Max(0, maxColumns));
            long totalRows = reader.Metadata?.NumRows ?? 0;
            long cellRows = shownColumns == 0 ? 0 : maxCells / shownColumns;
            long limit = Math.Min(totalRows, Math.Min(maxRows, cellRows));

            var headers = new List<string>(shownColumns);
            for (int column = 0; column < shownColumns; column++)
            {
                headers.Add($"{fields[column].Name} ({TypeName(fields[column])})");
            }

            var rows = new List<string[]>();
            for (int group = 0; group < reader.RowGroupCount && rows.Count < limit; group++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using ParquetRowGroupReader rowGroup = reader.OpenRowGroupReader(group);
                int groupRows = (int)rowGroup.RowCount;
                int take = (int)Math.Min(groupRows, limit - rows.Count);
                string[][] columns = new string[shownColumns][];
                for (int column = 0; column < shownColumns; column++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    columns[column] = fields[column] is DataField field
                        ? await ReadColumnAsync(rowGroup, field, groupRows, cancellationToken)
                        : Enumerable.Repeat(NestedCell, groupRows).ToArray();
                }

                for (int row = 0; row < take; row++)
                {
                    string[] cells = new string[shownColumns];
                    for (int column = 0; column < shownColumns; column++)
                    {
                        cells[column] = columns[column][row];
                    }

                    rows.Add(cells);
                }
            }

            return new ParquetPreview(headers, rows, totalRows, fields.Count);
        }
        catch (Exception ex) when (ex is not ParquetReadException and not OperationCanceledException)
        {
            throw new ParquetReadException($"The file could not be read as Parquet: {ex.Message}", ex);
        }
    }

    private static string TypeName(Field field) => field is DataField data
        ? DataTypeName(data.ClrType)
        : field.SchemaType.ToString().ToLowerInvariant();

    private static string DataTypeName(Type type) => Underlying(type) switch
    {
        var clr when clr == typeof(string) || clr == typeof(ReadOnlyMemory<char>) => "string",
        var clr when clr == typeof(bool) => "boolean",
        var clr when clr == typeof(sbyte) => "int8",
        var clr when clr == typeof(byte) => "uint8",
        var clr when clr == typeof(short) => "int16",
        var clr when clr == typeof(ushort) => "uint16",
        var clr when clr == typeof(int) => "int32",
        var clr when clr == typeof(uint) => "uint32",
        var clr when clr == typeof(long) => "int64",
        var clr when clr == typeof(ulong) => "uint64",
        var clr when clr == typeof(float) => "float32",
        var clr when clr == typeof(double) => "float64",
        var clr when clr == typeof(decimal) => "decimal",
        var clr when clr == typeof(DateTime) => "datetime",
        var clr when clr == typeof(TimeSpan) => "timespan",
        var clr when clr == typeof(Guid) => "uuid",
        var clr => clr.Name.ToLowerInvariant(),
    };

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    /// <summary>The cells of one column of one row group, as text, one per row of the group.</summary>
    private static async Task<string[]> ReadColumnAsync(ParquetRowGroupReader rowGroup, DataField field, int count, CancellationToken cancellationToken)
    {
        Type clr = Underlying(field.ClrType);
        if (clr == typeof(string) || clr == typeof(ReadOnlyMemory<char>))
        {
            return await ReadStringsAsync(rowGroup, field, count, cancellationToken);
        }

        if (clr == typeof(bool)) return await ReadValuesAsync<bool>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(sbyte)) return await ReadValuesAsync<sbyte>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(byte)) return await ReadValuesAsync<byte>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(short)) return await ReadValuesAsync<short>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(ushort)) return await ReadValuesAsync<ushort>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(int)) return await ReadValuesAsync<int>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(uint)) return await ReadValuesAsync<uint>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(long)) return await ReadValuesAsync<long>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(ulong)) return await ReadValuesAsync<ulong>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(float)) return await ReadValuesAsync<float>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(double)) return await ReadValuesAsync<double>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(decimal)) return await ReadValuesAsync<decimal>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(DateTime)) return await ReadValuesAsync<DateTime>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(TimeSpan)) return await ReadValuesAsync<TimeSpan>(rowGroup, field, count, cancellationToken);
        if (clr == typeof(Guid)) return await ReadValuesAsync<Guid>(rowGroup, field, count, cancellationToken);
        return Enumerable.Repeat(UnsupportedCell, count).ToArray();
    }

    private static async Task<string[]> ReadStringsAsync(ParquetRowGroupReader rowGroup, DataField field, int count, CancellationToken cancellationToken)
    {
        string?[] values = new string?[count];
        await rowGroup.ReadAsync(field, values.AsMemory(), null, cancellationToken);
        return values.Select(value => value ?? string.Empty).ToArray();
    }

    private static async Task<string[]> ReadValuesAsync<T>(ParquetRowGroupReader rowGroup, DataField field, int count, CancellationToken cancellationToken)
        where T : struct
    {
        string[] cells = new string[count];
        if (field.IsNullable)
        {
            T?[] values = new T?[count];
            await rowGroup.ReadAsync<T>(field, values.AsMemory(), null, cancellationToken);
            for (int row = 0; row < count; row++)
            {
                cells[row] = values[row] is { } value ? Text(value) : string.Empty;
            }
        }
        else
        {
            T[] values = new T[count];
            await rowGroup.ReadAsync<T>(field, values.AsMemory(), null, cancellationToken);
            for (int row = 0; row < count; row++)
            {
                cells[row] = Text(values[row]);
            }
        }

        return cells;
    }

    private static string Text<T>(T value) where T : struct => value switch
    {
        DateTime date when date.TimeOfDay == TimeSpan.Zero => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
