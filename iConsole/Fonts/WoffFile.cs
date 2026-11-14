using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;

namespace iConsole.Fonts;

/// <summary>
/// Unwraps a WOFF 1.0 web font into the TrueType or OpenType (sfnt) bytes it carries, so <see cref="FontFileReader"/>
/// and the preview read it like any installed font. Each table is zlib-compressed when that made it smaller, and is
/// stored as it is otherwise. The metadata and private blocks are not needed for the summary and are skipped.
/// WOFF2 (Brotli) is not handled.
/// </summary>
public static class WoffFile
{
    private const uint Signature = 0x774F4646; // 'wOFF'
    private const int HeaderLength = 44;
    private const int EntryLength = 20;
    private const int SfntHeaderLength = 12;
    private const int SfntEntryLength = 16;
    private const long MaxTableLength = 64L * 1024 * 1024;

    /// <summary>Whether the bytes begin with the WOFF signature.</summary>
    public static bool IsWoff(byte[] bytes) =>
        bytes.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(bytes) == Signature;

    /// <summary>The bytes unchanged when they are not WOFF, otherwise the sfnt font they wrap.</summary>
    public static byte[] Unwrap(byte[] bytes) => IsWoff(bytes) ? ToSfnt(bytes) : bytes;

    /// <summary>The sfnt font inside a WOFF file. Throws <see cref="FormatException"/> when the file is damaged.</summary>
    public static byte[] ToSfnt(byte[] bytes)
    {
        if (!IsWoff(bytes) || bytes.Length < HeaderLength)
        {
            throw new FormatException("the file does not start with a WOFF header");
        }

        uint flavor = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(12));
        if (count == 0 || HeaderLength + (long)count * EntryLength > bytes.Length)
        {
            throw new FormatException("the WOFF table directory is missing or cut short");
        }

        var tables = new List<(byte[] Tag, uint Checksum, byte[] Data)>(count);
        for (int i = 0; i < count; i++)
        {
            int entry = HeaderLength + i * EntryLength;
            byte[] tag = bytes[entry..(entry + 4)];
            long offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry + 4));
            long compressed = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry + 8));
            long original = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry + 12));
            uint checksum = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry + 16));
            if (original > MaxTableLength || compressed > original || offset + compressed > bytes.Length)
            {
                throw new FormatException("a WOFF table lies outside the file");
            }

            tables.Add((tag, checksum, TableData(bytes, (int)offset, (int)compressed, (int)original)));
        }

        // The sfnt header and directory come first, then each table padded to a four-byte boundary.
        int directoryEnd = SfntHeaderLength + count * SfntEntryLength;
        long total = directoryEnd;
        foreach (var table in tables)
        {
            total += Pad4(table.Data.Length);
        }

        if (total > int.MaxValue)
        {
            throw new FormatException("the unwrapped font is too large");
        }

        var sfnt = new byte[total];
        int power = 1 << (31 - BitOperations.LeadingZeroCount((uint)count));
        BinaryPrimitives.WriteUInt32BigEndian(sfnt, flavor);
        BinaryPrimitives.WriteUInt16BigEndian(sfnt.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(sfnt.AsSpan(6), (ushort)(power * 16));
        BinaryPrimitives.WriteUInt16BigEndian(sfnt.AsSpan(8), (ushort)BitOperations.Log2((uint)power));
        BinaryPrimitives.WriteUInt16BigEndian(sfnt.AsSpan(10), (ushort)(count * 16 - power * 16));

        int position = directoryEnd;
        for (int i = 0; i < tables.Count; i++)
        {
            var (tag, checksum, data) = tables[i];
            int record = SfntHeaderLength + i * SfntEntryLength;
            tag.CopyTo(sfnt, record);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(record + 4), checksum);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(record + 8), (uint)position);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(record + 12), (uint)data.Length);
            data.CopyTo(sfnt, position);
            position += Pad4(data.Length);
        }

        return sfnt;
    }

    private static byte[] TableData(byte[] bytes, int offset, int compressed, int original)
    {
        if (compressed == original)
        {
            return bytes[offset..(offset + original)];
        }

        try
        {
            using var input = new MemoryStream(bytes, offset, compressed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            var output = new byte[original];
            int filled = 0;
            while (filled < original)
            {
                int read = zlib.Read(output, filled, original - filled);
                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            if (filled != original)
            {
                throw new FormatException("a WOFF table is shorter than its declared length");
            }

            return output;
        }
        catch (InvalidDataException exception)
        {
            throw new FormatException("a WOFF table is not valid zlib data", exception);
        }
    }

    private static int Pad4(int length) => (length + 3) & ~3;
}
