using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using iConsole.Helper;

namespace iConsole.Dbf;

/// <summary>A field of a dBase table: its name, its type letter, and its width in bytes.</summary>
public sealed record DbfField(string Name, char Type, int Length, int DecimalCount);

/// <summary>
/// A dBase table (dBase III and IV, Visual FoxPro) read into its field names and rows. Every value is text: a date reads as
/// <c>2026-03-05</c>, a logical as <c>true</c> or <c>false</c>, an empty value as nothing. A memo field's text lives in a
/// separate <c>.dbt</c> file, which is not read, so a memo shows as <c>(memo)</c>.
/// </summary>
/// <param name="Version">The first header byte, which names the dBase variant.</param>
/// <param name="LastUpdated">The date the table was last written, or null when the header holds no valid date.</param>
/// <param name="Fields">The columns, in record order.</param>
/// <param name="Rows">The live records (deleted ones are not kept), as text, one value per field.</param>
/// <param name="DeletedCount">Records marked deleted, which are not in <paramref name="Rows"/>.</param>
/// <param name="CodePage">The code page the text was read with, from the header's language driver byte.</param>
/// <param name="Truncated">True when the table had more than <see cref="MaxRows"/> records and only the first were read.</param>
public sealed record DbfDocument(
    int Version,
    DateOnly? LastUpdated,
    IReadOnlyList<DbfField> Fields,
    IReadOnlyList<string[]> Rows,
    int DeletedCount,
    Encoding CodePage,
    bool Truncated)
{
    /// <summary>The most records read; a larger table keeps its first records and says so.</summary>
    public const int MaxRows = 500_000;

    private const int HeaderSize = 32;
    private const int FieldSize = 32;

    /// <summary>
    /// Reads a table from its bytes. Throws <see cref="FormatException"/> when the header is not a dBase header: a table too short
    /// for one, a header length or record length that does not fit the file, or field descriptors with no terminator.
    /// </summary>
    public static DbfDocument Parse(byte[] bytes)
    {
        if (bytes.Length < HeaderSize + 1)
        {
            throw new FormatException("the file is too short to hold a dBase header");
        }

        int version = bytes[0];
        int recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
        int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10));
        if (headerLength < HeaderSize + 1 || headerLength > bytes.Length || recordLength < 1)
        {
            throw new FormatException("the header lengths do not fit the file");
        }

        var fields = ReadFields(bytes, headerLength);
        int fieldsWidth = fields.Sum(field => field.Length);
        if (1 + fieldsWidth > recordLength)
        {
            throw new FormatException("the fields are wider than a record");
        }

        Encoding encoding = CodePageFor(bytes[29]);
        DateOnly? lastUpdated = DateFromHeader(bytes[1], bytes[2], bytes[3]);

        var rows = new List<string[]>();
        int deleted = 0;
        for (int index = 0; index < recordCount; index++)
        {
            long offset = headerLength + (long)index * recordLength;
            if (offset + recordLength > bytes.Length)
            {
                // The header counts more records than the file holds; the complete ones are what there is.
                break;
            }

            if (bytes[offset] == '*')
            {
                deleted++;
                continue;
            }

            if (rows.Count == MaxRows)
            {
                return new DbfDocument(version, lastUpdated, fields, rows, deleted, encoding, Truncated: true);
            }

            rows.Add(ReadRecord(bytes, (int)offset, fields, encoding));
        }

        return new DbfDocument(version, lastUpdated, fields, rows, deleted, encoding, Truncated: false);
    }

    /// <summary>The field descriptors, from byte 32 to the terminator byte 0x0D.</summary>
    private static List<DbfField> ReadFields(byte[] bytes, int headerLength)
    {
        var fields = new List<DbfField>();
        for (int position = HeaderSize; position + FieldSize <= headerLength && bytes[position] != 0x0D; position += FieldSize)
        {
            int nameEnd = Array.IndexOf(bytes, (byte)0, position, 11);
            int nameLength = nameEnd < 0 ? 11 : nameEnd - position;
            string name = Encoding.ASCII.GetString(bytes, position, nameLength);
            fields.Add(new DbfField(name, (char)bytes[position + 11], bytes[position + 16], bytes[position + 17]));
        }

        if (fields.Count == 0)
        {
            throw new FormatException("the table has no fields");
        }

        return fields;
    }

    private static string[] ReadRecord(byte[] bytes, int offset, IReadOnlyList<DbfField> fields, Encoding encoding)
    {
        var values = new string[fields.Count];
        int position = offset + 1;
        for (int i = 0; i < fields.Count; i++)
        {
            DbfField field = fields[i];
            values[i] = Value(bytes.AsSpan(position, field.Length), field, encoding);
            position += field.Length;
        }

        return values;
    }

    /// <summary>One value as text, by the field's type letter.</summary>
    private static string Value(ReadOnlySpan<byte> raw, DbfField field, Encoding encoding)
    {
        switch (field.Type)
        {
            case 'C':
                return Text(raw, encoding).TrimEnd(' ', '\0');

            case 'N':
            case 'F':
                return Text(raw, encoding).Trim(' ', '\0');

            case 'L':
                return char.ToUpperInvariant((char)raw[0]) switch
                {
                    'Y' or 'T' => "true",
                    'N' or 'F' => "false",
                    _ => string.Empty,
                };

            case 'D':
                string digits = Text(raw, encoding);
                return digits.Length == 8
                    && DateOnly.TryParseExact(digits, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : string.Empty;

            case 'M':
            case 'G':
            case 'P':
                return raw.ContainsAnyExcept((byte)' ') ? "(memo)" : string.Empty;

            case 'B' when raw.Length == 8:
                return BitConverter.ToDouble(raw).ToString("G", CultureInfo.InvariantCulture);

            case 'O' when raw.Length == 8:
                return BitConverter.ToDouble(raw).ToString("G", CultureInfo.InvariantCulture);

            case 'I' when raw.Length == 4:
                return BinaryPrimitives.ReadInt32LittleEndian(raw).ToString(CultureInfo.InvariantCulture);

            case 'Y' when raw.Length == 8:
                return (BinaryPrimitives.ReadInt64LittleEndian(raw) / 10000m).ToString("0.####", CultureInfo.InvariantCulture);

            case 'T' when raw.Length == 8:
                return DateTimeFromFoxPro(raw);

            default:
                return $"[{field.Type}]";
        }
    }

    /// <summary>A Visual FoxPro datetime: a Julian day and the milliseconds into it.</summary>
    private static string DateTimeFromFoxPro(ReadOnlySpan<byte> raw)
    {
        int julianDay = BinaryPrimitives.ReadInt32LittleEndian(raw);
        int milliseconds = BinaryPrimitives.ReadInt32LittleEndian(raw[4..]);
        if (julianDay <= 0)
        {
            return string.Empty;
        }

        // Julian day 1,721,426 is 1 January 0001 in the proleptic Gregorian calendar, the first day DateTime holds.
        DateTime date = new DateTime(1, 1, 1).AddDays(julianDay - 1721426);
        return date.AddMilliseconds(milliseconds).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string Text(ReadOnlySpan<byte> raw, Encoding encoding) => encoding.GetString(raw);

    /// <summary>The header's language driver byte names a code page; a byte this table does not know reads as Windows-1252.</summary>
    private static Encoding CodePageFor(byte languageDriver)
    {
        int page = languageDriver switch
        {
            0x01 => 437,
            0x02 => 850,
            0x03 or 0x57 => 1252,
            0x04 => 10000,
            0x64 => 852,
            0x65 => 866,
            0x66 => 865,
            0x67 => 861,
            0x6A => 737,
            0x6B => 857,
            0x78 => 950,
            0x79 => 949,
            0x7A => 936,
            0x7B => 932,
            0x7C => 874,
            0xC8 => 1250,
            0xC9 => 1251,
            _ => 1252,
        };

        return TextEncodings.ForCodePage(page) ?? Encoding.Latin1;
    }

    private static DateOnly? DateFromHeader(byte year, byte month, byte day)
    {
        try
        {
            return new DateOnly(1900 + year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
