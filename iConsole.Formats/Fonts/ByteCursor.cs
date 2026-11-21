using System.Buffers.Binary;

namespace iConsole.Formats.Fonts;

/// <summary>
/// A forward-only reader over a byte array with bounds checks. Every read that would pass the end throws
/// <see cref="FormatException"/>, so a damaged font fails in one place instead of with an index exception.
/// </summary>
internal sealed class ByteCursor(byte[] bytes)
{
    public int Position { get; private set; }

    public int Remaining => bytes.Length - Position;

    public byte ReadByte()
    {
        Require(1);
        return bytes[Position++];
    }

    public ushort ReadUInt16()
    {
        Require(2);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(Position));
        Position += 2;
        return value;
    }

    public short ReadInt16() => (short)ReadUInt16();

    public uint ReadUInt32()
    {
        Require(4);
        uint value = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(Position));
        Position += 4;
        return value;
    }

    /// <summary>A big-endian unsigned value at <paramref name="offset"/> bytes ahead, without moving the cursor.</summary>
    public ushort PeekUInt16(int offset)
    {
        Require(offset + 2);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(Position + offset));
    }

    /// <summary>The byte <paramref name="offset"/> bytes ahead, without moving the cursor.</summary>
    public byte Peek(int offset)
    {
        Require(offset + 1);
        return bytes[Position + offset];
    }

    /// <summary>The next <paramref name="count"/> bytes, copied out.</summary>
    public byte[] Take(int count)
    {
        Require(count);
        byte[] taken = bytes[Position..(Position + count)];
        Position += count;
        return taken;
    }

    public void Skip(int count)
    {
        Require(count);
        Position += count;
    }

    /// <summary>A UIntBase128 value (WOFF 2.0 directory lengths): one to five bytes, no leading zero, below 2^32.</summary>
    public uint ReadBase128()
    {
        if (Remaining > 0 && bytes[Position] == 0x80)
        {
            throw new FormatException("a UIntBase128 value starts with a zero byte");
        }

        ulong result = 0;
        for (int i = 0; i < 5; i++)
        {
            byte code = ReadByte();
            byte low = (byte)(code & 0x7F);
            result = (result << 7) | low;
            if (result > uint.MaxValue)
            {
                throw new FormatException("a UIntBase128 value exceeds 2^32 - 1");
            }

            if ((code & 0x80) == 0)
            {
                return (uint)result;
            }
        }

        throw new FormatException("a UIntBase128 value is longer than five bytes");
    }

    /// <summary>A 255UInt16 value (WOFF 2.0 glyph data): one byte below 253, or a marker followed by two or one more bytes.</summary>
    public int Read255UInt16()
    {
        byte code = ReadByte();
        return code switch
        {
            253 => ReadUInt16(),
            254 => ReadByte() + 506,
            255 => ReadByte() + 253,
            _ => code,
        };
    }

    private void Require(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw new FormatException("the font data is cut short");
        }
    }
}
