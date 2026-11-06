using System.Buffers.Binary;
using System.Text;

namespace iConsole.Services;

/// <summary>
/// The Huffman/CDIC decompression of a Kindle book (compression 17480, used by many KF8 and AZW3 files). The HUFF record
/// holds two tables: a 256-entry table indexed by the top byte of the next 32 bits, and a 32-entry table that gives a
/// code's longest length for the codes the first one does not settle. The CDIC records hold the dictionary: each entry
/// is a byte string that is either plain or itself Huffman-compressed, and a code selects an entry.
/// The table walk follows the algorithm in the MIT-licensed lingo-reader mobi-parser; this is an independent port.
/// </summary>
internal sealed class MobiHuffmanDecoder
{
    private const int MaxDepth = 32;

    private readonly (bool Found, int CodeLength, uint Value)[] _table1 = new (bool, int, uint)[256];
    private readonly (uint MaxCode, uint Value)[] _table2 = new (uint, uint)[33];
    private readonly List<(byte[] Data, bool Decompressed)> _dictionary = [];

    private MobiHuffmanDecoder()
    {
    }

    /// <summary>
    /// Loads the tables from the HUFF record at <paramref name="huffIndex"/> and the CDIC records after it.
    /// <paramref name="record"/> returns the bytes of a record by its index in the file.
    /// </summary>
    public static MobiHuffmanDecoder Load(Func<int, byte[]> record, uint huffIndex, uint dictionaryRecords)
    {
        MobiHuffmanDecoder decoder = new();
        byte[] huff = record(checked((int)huffIndex));
        if (huff.Length < 16 || Encoding.ASCII.GetString(huff, 0, 4) != "HUFF")
        {
            throw new InvalidDataException("the Huffman table record is missing");
        }

        int offset1 = BigEndian32(huff, 8);
        int offset2 = BigEndian32(huff, 12);
        for (int i = 0; i < 256; i++)
        {
            uint x = BigEndian32U(huff, offset1 + i * 4);
            decoder._table1[i] = ((x & 0x80) != 0, (int)(x & 0x1F), x >> 8);
        }

        for (int length = 1; length <= 32; length++)
        {
            int offset = offset2 + (length - 1) * 8;
            decoder._table2[length] = (BigEndian32U(huff, offset), BigEndian32U(huff, offset + 4));
        }

        for (uint i = 1; i < dictionaryRecords; i++)
        {
            decoder.LoadDictionaryRecord(record(checked((int)(huffIndex + i))));
        }

        return decoder;
    }

    /// <summary>Decompresses one text record.</summary>
    public byte[] Decode(byte[] data) => Decode(data, 0);

    private void LoadDictionaryRecord(byte[] cdic)
    {
        if (cdic.Length < 16 || Encoding.ASCII.GetString(cdic, 0, 4) != "CDIC")
        {
            throw new InvalidDataException("a dictionary record is missing");
        }

        int headerLength = BigEndian32(cdic, 4);
        int numEntries = BigEndian32(cdic, 8);
        int codeLength = BigEndian32(cdic, 12);
        if (codeLength is < 0 or > 16)
        {
            throw new InvalidDataException("a dictionary record has a bad code length");
        }

        int count = Math.Min(1 << codeLength, numEntries - _dictionary.Count);
        ReadOnlySpan<byte> buffer = cdic.AsSpan(headerLength);
        for (int i = 0; i < count; i++)
        {
            int offset = BinaryPrimitives.ReadUInt16BigEndian(buffer[(i * 2)..]);
            int x = BinaryPrimitives.ReadUInt16BigEndian(buffer[offset..]);
            int length = x & 0x7FFF;
            bool decompressed = (x & 0x8000) != 0;
            _dictionary.Add((buffer.Slice(offset + 2, length).ToArray(), decompressed));
        }
    }

    private byte[] Decode(byte[] data, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidDataException("a dictionary entry refers to itself");
        }

        using MemoryStream output = new();
        long bitLength = data.Length * 8L;
        for (long position = 0; position < bitLength;)
        {
            uint bits = Read32Bits(data, position);
            (bool found, int codeLength, uint value) = _table1[bits >> 24];
            if (!found)
            {
                if (codeLength is < 1 or > 32)
                {
                    throw new InvalidDataException("the Huffman table is corrupt");
                }

                while (codeLength < 32 && (bits >> (32 - codeLength)) < _table2[codeLength].MaxCode)
                {
                    codeLength++;
                }

                value = _table2[codeLength].Value;
            }

            position += codeLength;
            if (position > bitLength)
            {
                break;
            }

            long code = (long)value - (bits >> (32 - codeLength));
            if (code < 0 || code >= _dictionary.Count)
            {
                throw new InvalidDataException("a code is outside the dictionary");
            }

            (byte[] entry, bool decompressed) = _dictionary[(int)code];
            if (!decompressed)
            {
                entry = Decode(entry, depth + 1);
                _dictionary[(int)code] = (entry, true);
            }

            output.Write(entry);
        }

        return output.ToArray();
    }

    /// <summary>The 32 bits that start at bit <paramref name="from"/> of <paramref name="data"/>, most significant bit first; past the end they read as zero.</summary>
    internal static uint Read32Bits(byte[] data, long from)
    {
        long startByte = from >> 3;
        long end = from + 32;
        long endByte = end >> 3;
        ulong bits = 0;
        for (long b = startByte; b <= endByte; b++)
        {
            bits = (bits << 8) | (b < data.Length ? data[b] : (byte)0);
        }

        return (uint)((bits >> (8 - (int)(end & 7))) & 0xFFFFFFFF);
    }

    private static int BigEndian32(byte[] data, int offset) => (int)BigEndian32U(data, offset);

    private static uint BigEndian32U(byte[] data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            throw new InvalidDataException("a Huffman table lies outside its record");
        }

        return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
    }
}
