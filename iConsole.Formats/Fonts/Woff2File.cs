using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace iConsole.Formats.Fonts;

/// <summary>
/// Unwraps a WOFF 2.0 web font into the TrueType or OpenType (sfnt) bytes it carries, so <see cref="FontFileReader"/>
/// and the preview read it like any installed font. The tables sit in one Brotli stream. <c>glyf</c> and <c>loca</c>,
/// and <c>hmtx</c> when it is transformed, are rebuilt by <see cref="Woff2Transforms"/>; the others are copied as they are.
/// Font collections are not supported.
/// </summary>
public static class Woff2File
{
    private const uint Signature = 0x774F4632; // 'wOF2'
    private const uint CollectionFlavor = 0x74746366; // 'ttcf'
    private const int HeaderLength = 48;
    private const int UnknownTagIndex = 63;
    private const int HeadIndexToLocFormatOffset = 50;
    private const int HeadMinimumLength = 54;
    private const int HheaNumberOfHMetricsOffset = 34;
    private const long MaxFontLength = 64L * 1024 * 1024;

    // The 63 tags a WOFF 2.0 directory can name by index, in the order the specification gives them.
    private static readonly string[] KnownTags =
    [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG",
        "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC",
        "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc",
        "feat", "fmtx", "fvar", "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat",
        "Gloc", "Feat", "Sill",
    ];

    private sealed record DirectoryEntry(string Tag, bool Transformed, uint OrigLength, uint StoredLength);

    private sealed record Table(string Tag, bool Transformed, uint OrigLength, byte[] Data);

    /// <summary>Whether the bytes begin with the WOFF 2.0 signature.</summary>
    public static bool IsWoff2(byte[] bytes) =>
        bytes.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(bytes) == Signature;

    /// <summary>The sfnt font inside a WOFF 2.0 file. Throws <see cref="FormatException"/> when the file is damaged or unsupported.</summary>
    public static byte[] ToSfnt(byte[] bytes)
    {
        if (!IsWoff2(bytes) || bytes.Length < HeaderLength)
        {
            throw new FormatException("the file does not start with a WOFF2 header");
        }

        var header = new ByteCursor(bytes);
        header.Skip(4);
        uint flavor = header.ReadUInt32();
        uint length = header.ReadUInt32();
        int count = header.ReadUInt16();
        header.Skip(2); // reserved
        uint totalSfntSize = header.ReadUInt32();
        uint totalCompressedSize = header.ReadUInt32();

        if (flavor == CollectionFlavor)
        {
            throw new FormatException("font collections are not supported");
        }

        if (length > bytes.Length)
        {
            throw new FormatException("the WOFF2 file is cut short");
        }

        if (count == 0 || totalSfntSize > MaxFontLength)
        {
            throw new FormatException("the WOFF2 header is not usable");
        }

        var cursor = new ByteCursor(bytes);
        cursor.Skip(HeaderLength);
        var directory = new List<DirectoryEntry>(count);
        for (int i = 0; i < count; i++)
        {
            directory.Add(ReadDirectoryEntry(cursor));
        }

        if (directory.Select(entry => entry.Tag).Distinct(StringComparer.Ordinal).Count() != count)
        {
            throw new FormatException("the WOFF2 directory names a table twice");
        }

        if (totalCompressedSize > cursor.Remaining)
        {
            throw new FormatException("the WOFF2 table data lies outside the file");
        }

        long uncompressed = directory.Sum(entry => (long)entry.StoredLength);
        if (uncompressed > MaxFontLength)
        {
            throw new FormatException("the WOFF2 tables are too large");
        }

        byte[] stream = Inflate(cursor.Take((int)totalCompressedSize), uncompressed);
        var tables = Slice(directory, stream);

        return Assemble(flavor, tables);
    }

    private static DirectoryEntry ReadDirectoryEntry(ByteCursor cursor)
    {
        byte flags = cursor.ReadByte();
        int index = flags & 0x3F;
        string tag = index == UnknownTagIndex
            ? Encoding.Latin1.GetString(cursor.Take(4))
            : KnownTags[index];
        int version = flags >> 6;

        // glyf and loca are transformed unless version 3 (the null transform) is given; other tables only when version is not 0.
        bool transformed = tag is "glyf" or "loca" ? version != 3 : version != 0;
        uint origLength = cursor.ReadBase128();
        uint storedLength = origLength;
        if (transformed)
        {
            storedLength = cursor.ReadBase128();
            if (tag == "loca" && storedLength != 0)
            {
                throw new FormatException("the transformed loca table has a length");
            }
        }

        return new DirectoryEntry(tag, transformed, origLength, storedLength);
    }

    /// <summary>Cuts the decompressed stream into the tables the directory describes, in directory order.</summary>
    private static List<Table> Slice(List<DirectoryEntry> directory, byte[] stream)
    {
        var tables = new List<Table>(directory.Count);
        int offset = 0;
        foreach (var entry in directory)
        {
            byte[] data = stream[offset..(offset + (int)entry.StoredLength)];
            tables.Add(new Table(entry.Tag, entry.Transformed, entry.OrigLength, data));
            offset += (int)entry.StoredLength;
        }

        return tables;
    }

    private static byte[] Assemble(uint flavor, List<Table> tables)
    {
        var untransformed = tables.Where(table => !table.Transformed)
            .ToDictionary(table => table.Tag, table => table.Data, StringComparer.Ordinal);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        Woff2Transforms.GlyfResult? glyf = null;
        var glyfTable = tables.FirstOrDefault(table => table.Tag == "glyf");
        var locaTable = tables.FirstOrDefault(table => table.Tag == "loca");
        if (glyfTable is { Transformed: true })
        {
            if (locaTable is null)
            {
                throw new FormatException("the transformed glyf table has no loca table");
            }

            glyf = Woff2Transforms.RebuildGlyf(glyfTable.Data);
            if (locaTable.OrigLength != glyf.Loca.Length)
            {
                throw new FormatException("the reconstructed loca table has the wrong size");
            }

            result["glyf"] = glyf.Glyf;
            result["loca"] = glyf.Loca;
        }
        else if (locaTable is { Transformed: true })
        {
            throw new FormatException("a transformed loca table needs a transformed glyf table");
        }

        foreach (var table in tables)
        {
            if (result.ContainsKey(table.Tag))
            {
                continue;
            }

            if (!table.Transformed)
            {
                result[table.Tag] = table.Data;
            }
            else if (table.Tag == "hmtx")
            {
                if (glyf is null)
                {
                    throw new FormatException("a transformed hmtx table needs a transformed glyf table");
                }

                byte[] hmtx = Woff2Transforms.RebuildHmtx(table.Data, NumberOfHMetrics(untransformed), glyf.XMin);
                if (hmtx.Length != table.OrigLength)
                {
                    throw new FormatException("the reconstructed hmtx table has the wrong size");
                }

                result["hmtx"] = hmtx;
            }
            else
            {
                throw new FormatException($"the transform of the {table.Tag} table is unknown");
            }
        }

        // The head table records the loca format, which the transformed glyf table states; keep the two in step.
        if (glyf is not null && result.TryGetValue("head", out byte[]? head) && head.Length >= HeadMinimumLength)
        {
            head = (byte[])head.Clone();
            BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(HeadIndexToLocFormatOffset), (short)glyf.IndexFormat);
            result["head"] = head;
        }

        return SfntBuilder.Build(flavor, result.Select(pair => (pair.Key, pair.Value)));
    }

    private static int NumberOfHMetrics(Dictionary<string, byte[]> untransformed)
    {
        if (!untransformed.TryGetValue("hhea", out byte[]? hhea) || hhea.Length < HheaNumberOfHMetricsOffset + 2)
        {
            throw new FormatException("the transformed hmtx table needs an hhea table");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(hhea.AsSpan(HheaNumberOfHMetricsOffset));
    }

    private static byte[] Inflate(byte[] compressed, long expected)
    {
        var output = new byte[(int)expected];
        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            int filled = 0;
            while (filled < output.Length)
            {
                int read = brotli.Read(output, filled, output.Length - filled);
                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            if (filled != output.Length || brotli.ReadByte() != -1)
            {
                throw new FormatException("the WOFF2 table data is not the size its directory gives");
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            // The Brotli decoder reports damaged input as either of these, depending on where it fails.
            throw new FormatException("the WOFF2 table data is not valid Brotli", exception);
        }

        return output;
    }
}
