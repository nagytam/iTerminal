using System.Text;

namespace iConsole.IWork;

/// <summary>One field of a protobuf message as it appears on the wire: its number, its wire type and its value.</summary>
internal readonly record struct WireField(int Number, int WireType, ulong Varint, ReadOnlyMemory<byte> Bytes);

/// <summary>
/// Reads protobuf messages without generated classes. iWork's archives are protobuf, and the text reader needs only a few
/// fields of a few messages, so this walks the wire format and picks the fields by number. Unknown fields are kept in
/// the list and ignored by callers, which is how a newer archive stays readable.
/// </summary>
internal static class ProtobufWire
{
    public static List<WireField> Parse(ReadOnlyMemory<byte> data)
    {
        List<WireField> fields = [];
        ReadOnlySpan<byte> span = data.Span;
        int position = 0;
        while (position < span.Length)
        {
            ulong key = ReadVarint(span, ref position);
            int number = (int)(key >> 3);
            int wireType = (int)(key & 7);
            switch (wireType)
            {
                case 0:
                    fields.Add(new WireField(number, wireType, ReadVarint(span, ref position), default));
                    break;
                case 1:
                    fields.Add(new WireField(number, wireType, 0, Take(data, ref position, 8)));
                    break;
                case 2:
                {
                    ulong length = ReadVarint(span, ref position);
                    if (length > (ulong)(span.Length - position))
                    {
                        throw new InvalidDataException("a field runs past the end of its message");
                    }

                    fields.Add(new WireField(number, wireType, 0, Take(data, ref position, (int)length)));
                    break;
                }
                case 5:
                    fields.Add(new WireField(number, wireType, 0, Take(data, ref position, 4)));
                    break;
                default:
                    throw new InvalidDataException($"wire type {wireType} is not used by iWork archives");
            }
        }

        return fields;
    }

    /// <summary>The first varint field with this number, or null.</summary>
    public static ulong? Varint(List<WireField> fields, int number)
    {
        foreach (WireField field in fields)
        {
            if (field.Number == number && field.WireType == 0)
            {
                return field.Varint;
            }
        }

        return null;
    }

    /// <summary>The first length-delimited field with this number, or null. A string, a sub-message and packed values all come this way.</summary>
    public static ReadOnlyMemory<byte>? Bytes(List<WireField> fields, int number)
    {
        foreach (WireField field in fields)
        {
            if (field.Number == number && field.WireType == 2)
            {
                return field.Bytes;
            }
        }

        return null;
    }

    /// <summary>Every length-delimited field with this number, in order.</summary>
    public static IEnumerable<ReadOnlyMemory<byte>> AllBytes(List<WireField> fields, int number)
    {
        foreach (WireField field in fields)
        {
            if (field.Number == number && field.WireType == 2)
            {
                yield return field.Bytes;
            }
        }
    }

    /// <summary>The first length-delimited field with this number read as UTF-8 text, or null.</summary>
    public static string? String(List<WireField> fields, int number) =>
        Bytes(fields, number) is { } bytes ? Encoding.UTF8.GetString(bytes.Span) : null;

    public static ulong ReadVarint(ReadOnlySpan<byte> span, ref int position)
    {
        ulong value = 0;
        for (int shift = 0; shift < 64; shift += 7)
        {
            if (position >= span.Length)
            {
                throw new InvalidDataException("a varint runs past the end of its message");
            }

            byte b = span[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("a varint is too long");
    }

    private static ReadOnlyMemory<byte> Take(ReadOnlyMemory<byte> data, ref int position, int length)
    {
        if (length < 0 || position + length > data.Length)
        {
            throw new InvalidDataException("a field runs past the end of its message");
        }

        ReadOnlyMemory<byte> slice = data.Slice(position, length);
        position += length;
        return slice;
    }
}
