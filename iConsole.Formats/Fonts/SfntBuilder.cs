using System.Buffers.Binary;
using System.Numerics;

namespace iConsole.Fonts;

/// <summary>
/// Lays tables out as an sfnt font (TrueType or OpenType): the offset table, the directory sorted by tag, then each table
/// padded to a four-byte boundary with its checksum recorded. Both the WOFF and WOFF2 unwrappers build their output here.
/// </summary>
internal static class SfntBuilder
{
    private const int OffsetTableLength = 12;
    private const int DirectoryEntryLength = 16;
    private const int HeadChecksumAdjustmentOffset = 8;
    private const uint MagicChecksum = 0xB1B0AFBA;

    public static byte[] Build(uint flavor, IEnumerable<(string Tag, byte[] Data)> tables)
    {
        var sorted = tables.OrderBy(table => table.Tag, StringComparer.Ordinal).ToList();
        int count = sorted.Count;
        if (count == 0 || count > ushort.MaxValue)
        {
            throw new FormatException("the font has no usable tables");
        }

        long total = OffsetTableLength + (long)count * DirectoryEntryLength;
        foreach (var (tag, data) in sorted)
        {
            if (tag.Length != 4)
            {
                throw new FormatException("a table tag is not four characters long");
            }

            total += Pad4(data.Length);
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

        int position = OffsetTableLength + count * DirectoryEntryLength;
        int headPosition = -1;
        for (int i = 0; i < sorted.Count; i++)
        {
            var (tag, data) = sorted[i];
            int record = OffsetTableLength + i * DirectoryEntryLength;
            System.Text.Encoding.ASCII.GetBytes(tag, sfnt.AsSpan(record, 4));

            // The checksum of head is taken with its checkSumAdjustment set to zero, as the specification requires.
            if (tag == "head" && data.Length >= HeadChecksumAdjustmentOffset + 4)
            {
                data = (byte[])data.Clone();
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(HeadChecksumAdjustmentOffset), 0);
                headPosition = position;
            }

            data.CopyTo(sfnt, position);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(record + 4), Checksum(sfnt.AsSpan(position, Pad4(data.Length))));
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(record + 8), (uint)position);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(record + 12), (uint)data.Length);
            position += Pad4(data.Length);
        }

        // The whole font, head included, must sum to the magic constant once its checkSumAdjustment is in place.
        if (headPosition >= 0)
        {
            uint adjustment = MagicChecksum - Checksum(sfnt);
            BinaryPrimitives.WriteUInt32BigEndian(sfnt.AsSpan(headPosition + HeadChecksumAdjustmentOffset), adjustment);
        }

        return sfnt;
    }

    /// <summary>The sum of the big-endian 32-bit words of a table, as the sfnt directory records it.</summary>
    private static uint Checksum(ReadOnlySpan<byte> padded)
    {
        uint sum = 0;
        for (int i = 0; i + 4 <= padded.Length; i += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(padded[i..]);
        }

        return sum;
    }

    private static int Pad4(int length) => (length + 3) & ~3;
}
