using System.Buffers.Binary;
using System.Text;

namespace iConsole.Services;

/// <summary>
/// Reads the text of a Kindle MOBI or AZW (Palm database) book into lines. The text records are PalmDOC-compressed
/// HTML: each record is first stripped of the trailing entries the MOBI header declares, then decompressed, and the
/// markup goes through the shared HTML reader. Images, the table of contents and the book's metadata are not shown.
/// Books that use Huffman compression (KF8 and AZW3) are reported, not read.
/// </summary>
public static class MobiTextReader
{
    private const int PalmDatabaseHeaderLength = 78;
    private const int RecordEntryLength = 8;
    private const ushort NoCompression = 1;
    private const ushort PalmDocCompression = 2;
    private const ushort HuffmanCompression = 17480;
    private const uint Utf8Encoding = 65001;
    private const int MobiHeaderOffset = 16;

    public static IReadOnlyList<string> Read(byte[] content)
    {
        try
        {
            if (content.Length < PalmDatabaseHeaderLength)
            {
                return ["[Could not read the book: the file is too short]"];
            }

            int recordCount = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(76));
            if (recordCount == 0 || content.Length < PalmDatabaseHeaderLength + recordCount * RecordEntryLength)
            {
                return ["[Could not read the book: the record list is missing]"];
            }

            List<int> offsets = [.. Enumerable.Range(0, recordCount)
                .Select(i => (int)BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(PalmDatabaseHeaderLength + i * RecordEntryLength)))];

            byte[] first = Record(content, offsets, 0);
            if (first.Length < MobiHeaderOffset + 0x14 || Encoding.ASCII.GetString(first, MobiHeaderOffset, 4) != "MOBI")
            {
                return ["[Could not read the book: it has no MOBI header]"];
            }

            ushort compression = BinaryPrimitives.ReadUInt16BigEndian(first);
            ushort textRecords = BinaryPrimitives.ReadUInt16BigEndian(first.AsSpan(8));
            uint mobiLength = BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(MobiHeaderOffset + 4));
            uint encodingCode = BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(MobiHeaderOffset + 0x0C));
            ushort extraFlags = mobiLength >= 0xF4 && first.Length >= MobiHeaderOffset + 0xF4
                ? BinaryPrimitives.ReadUInt16BigEndian(first.AsSpan(MobiHeaderOffset + 0xF2))
                : (ushort)0;

            if (compression == HuffmanCompression)
            {
                return ["[This Kindle book uses Huffman compression (KF8/AZW3), which is not read]"];
            }

            if (compression is not (NoCompression or PalmDocCompression))
            {
                return [$"[Could not read the book: unknown compression {compression}]"];
            }

            using MemoryStream text = new();
            int last = Math.Min(textRecords, recordCount - 1);
            for (int i = 1; i <= last; i++)
            {
                byte[] record = StripTrailingEntries(Record(content, offsets, i), extraFlags);
                byte[] bytes = compression == PalmDocCompression ? PalmDocDecompress(record) : record;
                text.Write(bytes);
            }

            Encoding encoding = encodingCode == Utf8Encoding ? Encoding.UTF8 : Encoding.GetEncoding(1252);
            return HtmlTextExtractor.Lines(encoding.GetString(text.ToArray()));
        }
        catch (Exception exception)
        {
            return [$"[Could not read the book: {exception.Message}]"];
        }
    }

    /// <summary>The bytes of record <paramref name="index"/>: from its offset to the next record's, or the end of the file.</summary>
    private static byte[] Record(byte[] content, List<int> offsets, int index)
    {
        int start = offsets[index];
        int end = index + 1 < offsets.Count ? offsets[index + 1] : content.Length;
        if (start < 0 || end < start || end > content.Length)
        {
            throw new InvalidDataException($"record {index} lies outside the file");
        }

        return content[start..end];
    }

    /// <summary>
    /// Removes the trailing entries of a text record: for each flag bit from 1 up, an entry whose backward-encoded size
    /// is read from the end, then, for bit 0, the overlap of a split multi-byte character. This is the order and sizing
    /// KindleUnpack uses.
    /// </summary>
    internal static byte[] StripTrailingEntries(byte[] record, ushort flags)
    {
        int trailing = 0;
        for (int bit = 1; bit < 16; bit++)
        {
            if (((flags >> bit) & 1) != 0)
            {
                trailing += TrailingEntrySize(record, record.Length - trailing);
            }
        }

        if ((flags & 1) != 0 && record.Length - trailing > 0)
        {
            trailing += (record[record.Length - trailing - 1] & 0x3) + 1;
        }

        return record[..Math.Max(0, record.Length - trailing)];
    }

    /// <summary>The size of the trailing entry that ends at <paramref name="end"/>: 7 bits a byte, read backwards.</summary>
    private static int TrailingEntrySize(byte[] record, int end)
    {
        int result = 0;
        int bitPosition = 0;
        int position = end;
        while (position > 0)
        {
            byte value = record[position - 1];
            result |= (value & 0x7F) << bitPosition;
            bitPosition += 7;
            position--;
            if ((value & 0x80) != 0 || bitPosition >= 28)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>PalmDOC LZ77: literal bytes, runs of literal bytes, back-references, and space plus character pairs.</summary>
    internal static byte[] PalmDocDecompress(byte[] data)
    {
        List<byte> output = new(data.Length * 2);
        int i = 0;
        while (i < data.Length)
        {
            byte c = data[i++];
            if (c == 0 || c >= 0x09 && c <= 0x7F)
            {
                output.Add(c);
            }
            else if (c <= 0x08)
            {
                // The next c bytes are copied as they are.
                for (int k = 0; k < c && i < data.Length; k++)
                {
                    output.Add(data[i++]);
                }
            }
            else if (c >= 0xC0)
            {
                // A space and the character with its top bit flipped.
                output.Add((byte)' ');
                output.Add((byte)(c ^ 0x80));
            }
            else
            {
                // A back-reference: 11 bits of distance back, 3 bits of length less three.
                if (i >= data.Length)
                {
                    break;
                }

                int pair = (c << 8) | data[i++];
                int distance = (pair >> 3) & 0x7FF;
                int length = (pair & 0x7) + 3;
                if (distance == 0 || distance > output.Count)
                {
                    break;
                }

                for (int k = 0; k < length; k++)
                {
                    output.Add(output[output.Count - distance]);
                }
            }
        }

        return [.. output];
    }
}
