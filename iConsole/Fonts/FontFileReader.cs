using System.Buffers.Binary;
using System.Text;

namespace iConsole.Fonts;

/// <summary>What a TrueType or OpenType font file says about itself: its names, its size and its design.</summary>
/// <param name="Outlines">TrueType (quadratic outlines) or OpenType with CFF (cubic outlines).</param>
/// <param name="FamilyName">The family name, for example <c>Sample Sans</c>; null when the name table has none.</param>
/// <param name="SubfamilyName">The style within the family, for example <c>Bold Italic</c>.</param>
/// <param name="FullName">The full name, for example <c>Sample Sans Bold Italic</c>.</param>
/// <param name="PostScriptName">The PostScript name, the one a document refers to the font by.</param>
/// <param name="Version">The version string the font declares.</param>
/// <param name="Copyright">The copyright notice.</param>
/// <param name="Designer">The designer's name.</param>
/// <param name="Manufacturer">The vendor's name.</param>
/// <param name="License">The license text, when the font names one.</param>
/// <param name="UnitsPerEm">The design grid: the number of units across one em.</param>
/// <param name="GlyphCount">The glyphs in the font.</param>
/// <param name="CharacterCount">The code points the character map covers (counted from its ranges, not from which of them have a glyph).</param>
/// <param name="Weight">The OS/2 weight class, 100 (thin) to 900 (black); 0 when the font has no OS/2 table.</param>
/// <param name="WidthClass">The OS/2 width class, 1 (ultra-condensed) to 9 (ultra-expanded); 0 when unknown.</param>
/// <param name="Bold">True when the font declares itself bold.</param>
/// <param name="Italic">True when the font declares itself italic.</param>
/// <param name="Created">When the font was created, from the head table.</param>
/// <param name="Modified">When the font was last changed, from the head table.</param>
/// <param name="Embedding">What the font's license allows when a document embeds it.</param>
public sealed record FontInfo(
    string Outlines,
    string? FamilyName,
    string? SubfamilyName,
    string? FullName,
    string? PostScriptName,
    string? Version,
    string? Copyright,
    string? Designer,
    string? Manufacturer,
    string? License,
    int UnitsPerEm,
    int GlyphCount,
    int CharacterCount,
    int Weight,
    int WidthClass,
    bool Bold,
    bool Italic,
    DateTime? Created,
    DateTime? Modified,
    string Embedding);

/// <summary>
/// Reads the tables of a TrueType or OpenType font that a viewer shows: the name table, the head, maxp, OS/2 and cmap tables.
/// It reads the bytes only and never draws or loads the font. Every offset is checked against the file, so a damaged table is
/// skipped rather than read past. A collection (<c>.ttc</c>) and a file with no tables are refused.
/// </summary>
public static class FontFileReader
{
    private const uint TrueTypeVersion = 0x00010000;
    private const uint TrueTypeTag = 0x74727565; // 'true'
    private const uint OpenTypeTag = 0x4F54544F; // 'OTTO'

    /// <summary>The font's information. Throws <see cref="FormatException"/> when the bytes are not an sfnt font.</summary>
    public static FontInfo Read(byte[] bytes)
    {
        if (bytes.Length < 12)
        {
            throw new FormatException("the file is too short to hold a font");
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        string outlines = version switch
        {
            TrueTypeVersion or TrueTypeTag => "TrueType",
            OpenTypeTag => "OpenType (CFF)",
            _ => throw new FormatException("the file does not start with a font header"),
        };

        var tables = Directory(bytes);
        if (tables.Count == 0)
        {
            throw new FormatException("the font has no tables");
        }

        ReadOnlySpan<byte> head = Table(bytes, tables, "head");
        ReadOnlySpan<byte> maxp = Table(bytes, tables, "maxp");
        ReadOnlySpan<byte> os2 = Table(bytes, tables, "OS/2");
        var names = Names(bytes, tables);

        int unitsPerEm = head.Length >= 20 ? BinaryPrimitives.ReadUInt16BigEndian(head[18..]) : 0;
        DateTime? created = head.Length >= 28 ? FromMacTime(BinaryPrimitives.ReadInt64BigEndian(head[20..])) : null;
        DateTime? modified = head.Length >= 36 ? FromMacTime(BinaryPrimitives.ReadInt64BigEndian(head[28..])) : null;
        ushort macStyle = head.Length >= 46 ? BinaryPrimitives.ReadUInt16BigEndian(head[44..]) : (ushort)0;

        int glyphs = maxp.Length >= 6 ? BinaryPrimitives.ReadUInt16BigEndian(maxp[4..]) : 0;

        int weight = 0, widthClass = 0, embeddingFlags = -1;
        bool bold = (macStyle & 1) != 0, italic = (macStyle & 2) != 0;
        if (os2.Length >= 64)
        {
            embeddingFlags = BinaryPrimitives.ReadUInt16BigEndian(os2[8..]);
            weight = BinaryPrimitives.ReadUInt16BigEndian(os2[4..]);
            widthClass = BinaryPrimitives.ReadUInt16BigEndian(os2[6..]);
            ushort selection = BinaryPrimitives.ReadUInt16BigEndian(os2[62..]);
            italic |= (selection & 0x01) != 0;
            bold |= (selection & 0x20) != 0;
        }

        return new FontInfo(
            Outlines: outlines,
            FamilyName: names.Get(1),
            SubfamilyName: names.Get(2),
            FullName: names.Get(4),
            PostScriptName: names.Get(6),
            Version: names.Get(5),
            Copyright: names.Get(0),
            Designer: names.Get(9),
            Manufacturer: names.Get(8),
            License: names.Get(13),
            UnitsPerEm: unitsPerEm,
            GlyphCount: glyphs,
            CharacterCount: CharacterCount(bytes, Table(bytes, tables, "cmap")),
            Weight: weight,
            WidthClass: widthClass,
            Bold: bold,
            Italic: italic,
            Created: created,
            Modified: modified,
            Embedding: Embedding(embeddingFlags));
    }

    /// <summary>The table directory: each tag with the offset and length of its table, for the tables whose bytes lie in the file.</summary>
    private static Dictionary<string, (int Offset, int Length)> Directory(byte[] bytes)
    {
        var tables = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + i * 16;
            if (record + 16 > bytes.Length)
            {
                break;
            }

            string tag = Encoding.ASCII.GetString(bytes, record, 4);
            long offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record + 8));
            long length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record + 12));
            if (offset + length <= bytes.Length)
            {
                tables.TryAdd(tag, ((int)offset, (int)length));
            }
        }

        return tables;
    }

    private static ReadOnlySpan<byte> Table(byte[] bytes, Dictionary<string, (int Offset, int Length)> tables, string tag) =>
        tables.TryGetValue(tag, out (int Offset, int Length) table) ? bytes.AsSpan(table.Offset, table.Length) : ReadOnlySpan<byte>.Empty;

    /// <summary>The name table's strings, by name ID: the English (Windows) one where there is one, else another language, else the Mac one.</summary>
    private static NameTable Names(byte[] bytes, Dictionary<string, (int Offset, int Length)> tables)
    {
        ReadOnlySpan<byte> name = Table(bytes, tables, "name");
        var result = new NameTable();
        if (name.Length < 6)
        {
            return result;
        }

        int count = BinaryPrimitives.ReadUInt16BigEndian(name[2..]);
        int stringOffset = BinaryPrimitives.ReadUInt16BigEndian(name[4..]);
        for (int i = 0; i < count; i++)
        {
            int record = 6 + i * 12;
            if (record + 12 > name.Length)
            {
                break;
            }

            int platform = BinaryPrimitives.ReadUInt16BigEndian(name[record..]);
            int language = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 4)..]);
            int nameId = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 6)..]);
            int length = BinaryPrimitives.ReadUInt16BigEndian(name[(record + 8)..]);
            int offset = stringOffset + BinaryPrimitives.ReadUInt16BigEndian(name[(record + 10)..]);
            if (offset + length > name.Length)
            {
                continue;
            }

            ReadOnlySpan<byte> raw = name.Slice(offset, length);
            // Windows (3) and Unicode (0) strings are UTF-16 big-endian; Macintosh (1) strings are one byte each.
            string text = platform is 0 or 3
                ? Encoding.BigEndianUnicode.GetString(raw).Trim('\0', ' ')
                : Encoding.Latin1.GetString(raw).Trim('\0', ' ');
            int rank = platform == 3 && language == 0x0409 ? 3 : platform == 3 ? 2 : platform == 0 ? 1 : 0;
            result.Offer(nameId, text, rank);
        }

        return result;
    }

    /// <summary>The number of code points the character map's ranges cover, from its format 12 or format 4 subtable.</summary>
    private static int CharacterCount(byte[] bytes, ReadOnlySpan<byte> cmap)
    {
        if (cmap.Length < 4)
        {
            return 0;
        }

        // Prefer the full Unicode subtable (format 12), then the Unicode BMP one (format 4), then any other.
        int best = 0, bestRank = -1;
        int subtables = BinaryPrimitives.ReadUInt16BigEndian(cmap[2..]);
        for (int i = 0; i < subtables; i++)
        {
            int record = 4 + i * 8;
            if (record + 8 > cmap.Length)
            {
                break;
            }

            int platform = BinaryPrimitives.ReadUInt16BigEndian(cmap[record..]);
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap[(record + 4)..]);
            if (offset + 6 > cmap.Length)
            {
                continue;
            }

            int format = BinaryPrimitives.ReadUInt16BigEndian(cmap[offset..]);
            int rank = format == 12 ? 3 : format == 4 && platform == 3 ? 2 : format == 4 ? 1 : 0;
            if (rank <= bestRank || (format != 4 && format != 12))
            {
                continue;
            }

            int covered = format == 12 ? Format12(cmap, offset) : Format4(cmap, offset);
            best = covered;
            bestRank = rank;
        }

        return best;
    }

    private static int Format4(ReadOnlySpan<byte> cmap, int offset)
    {
        if (offset + 14 > cmap.Length)
        {
            return 0;
        }

        int segments = BinaryPrimitives.ReadUInt16BigEndian(cmap[(offset + 6)..]) / 2;
        int endCodes = offset + 14;
        int startCodes = endCodes + segments * 2 + 2;
        if (startCodes + segments * 2 > cmap.Length)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < segments; i++)
        {
            int end = BinaryPrimitives.ReadUInt16BigEndian(cmap[(endCodes + i * 2)..]);
            int start = BinaryPrimitives.ReadUInt16BigEndian(cmap[(startCodes + i * 2)..]);
            if (end >= start && end != 0xFFFF)
            {
                total += end - start + 1;
            }
        }

        return total;
    }

    private static int Format12(ReadOnlySpan<byte> cmap, int offset)
    {
        if (offset + 16 > cmap.Length)
        {
            return 0;
        }

        long groups = BinaryPrimitives.ReadUInt32BigEndian(cmap[(offset + 12)..]);
        long total = 0;
        for (long i = 0; i < groups; i++)
        {
            int group = offset + 16 + (int)(i * 12);
            if (group + 12 > cmap.Length)
            {
                break;
            }

            long start = BinaryPrimitives.ReadUInt32BigEndian(cmap[group..]);
            long end = BinaryPrimitives.ReadUInt32BigEndian(cmap[(group + 4)..]);
            if (end >= start)
            {
                total += end - start + 1;
            }
        }

        return (int)Math.Min(total, int.MaxValue);
    }

    /// <summary>What the OS/2 embedding flags allow. -1 means the font has no OS/2 table.</summary>
    private static string Embedding(int flags) => flags switch
    {
        -1 => "not stated",
        0 => "installable",
        _ when (flags & 0x0002) != 0 => "restricted licence: no embedding in documents",
        _ when (flags & 0x0200) != 0 => "bitmap embedding only",
        _ => "preview and print",
    };

    /// <summary>The head table's dates count seconds from 1 January 1904. A value out of range reads as unknown.</summary>
    private static DateTime? FromMacTime(long seconds)
    {
        if (seconds <= 0)
        {
            return null;
        }

        try
        {
            return new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>The best string seen for each name ID.</summary>
    private sealed class NameTable
    {
        private readonly Dictionary<int, (string Text, int Rank)> _names = [];

        public void Offer(int nameId, string text, int rank)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (!_names.TryGetValue(nameId, out (string Text, int Rank) current) || rank > current.Rank)
            {
                _names[nameId] = (text, rank);
            }
        }

        public string? Get(int nameId) => _names.TryGetValue(nameId, out (string Text, int Rank) entry) ? entry.Text : null;
    }
}
