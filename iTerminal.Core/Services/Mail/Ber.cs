using System.Text;

namespace iTerminal.Core.Services.Mail;

/// <summary>
/// The part of ASN.1 BER that LDAP uses: a tag, a length and a value, with the integers, strings and
/// sequences LDAP messages are built from. Only what the address-book search needs is here.
/// </summary>
internal static class Ber
{
    public const byte Boolean = 0x01;
    public const byte Integer = 0x02;
    public const byte OctetString = 0x04;
    public const byte Enumerated = 0x0A;
    public const byte Sequence = 0x30;
    public const byte Set = 0x31;

    public static byte[] Tlv(byte tag, byte[] value)
    {
        var length = Length(value.Length);
        var result = new byte[1 + length.Length + value.Length];
        result[0] = tag;
        length.CopyTo(result, 1);
        value.CopyTo(result, 1 + length.Length);
        return result;
    }

    public static byte[] Tlv(byte tag, params byte[][] parts) => Tlv(tag, Concat(parts));

    public static byte[] Int(long value, byte tag = Integer)
    {
        var bytes = new List<byte>();
        var v = value;
        while (true)
        {
            bytes.Insert(0, (byte)(v & 0xFF));
            v >>= 8;
            if ((v == 0 && (bytes[0] & 0x80) == 0) || (v == -1 && (bytes[0] & 0x80) != 0))
            {
                break;
            }
        }

        return Tlv(tag, [.. bytes]);
    }

    public static byte[] Str(string value) => Tlv(OctetString, Encoding.UTF8.GetBytes(value));

    public static byte[] Bool(bool value) => Tlv(Boolean, [value ? (byte)0xFF : (byte)0x00]);

    public static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }

    /// <summary>Splits a value into its consecutive elements: each is a tag and its content.</summary>
    public static List<(byte Tag, byte[] Value)> Elements(byte[] data)
    {
        var result = new List<(byte, byte[])>();
        var position = 0;
        while (position < data.Length)
        {
            var tag = data[position++];
            var length = ReadLength(data, ref position);
            if (position + length > data.Length)
            {
                throw new FormatException("A BER element runs past the end of its value.");
            }

            result.Add((tag, data[position..(position + length)]));
            position += length;
        }

        return result;
    }

    public static long DecodeInt(byte[] value)
    {
        long result = value.Length > 0 && (value[0] & 0x80) != 0 ? -1 : 0;
        foreach (var b in value)
        {
            result = (result << 8) | b;
        }

        return result;
    }

    public static string DecodeString(byte[] value) => Encoding.UTF8.GetString(value);

    /// <summary>Reads one whole element from a stream: its tag, then its length, then its value.</summary>
    public static async Task<(byte Tag, byte[] Value)> ReadElementAsync(Stream stream, CancellationToken cancellationToken)
    {
        var tag = await ReadByteAsync(stream, cancellationToken);
        var first = await ReadByteAsync(stream, cancellationToken);
        int length;
        if ((first & 0x80) == 0)
        {
            length = first;
        }
        else
        {
            var count = first & 0x7F;
            length = 0;
            for (var i = 0; i < count; i++)
            {
                length = (length << 8) | await ReadByteAsync(stream, cancellationToken);
            }
        }

        var value = new byte[length];
        await stream.ReadExactlyAsync(value, cancellationToken);
        return (tag, value);
    }

    private static async Task<byte> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return buffer[0];
    }

    private static int ReadLength(byte[] data, ref int position)
    {
        var first = data[position++];
        if ((first & 0x80) == 0)
        {
            return first;
        }

        var count = first & 0x7F;
        var length = 0;
        for (var i = 0; i < count; i++)
        {
            length = (length << 8) | data[position++];
        }

        return length;
    }

    private static byte[] Length(int length)
    {
        if (length < 0x80)
        {
            return [(byte)length];
        }

        var bytes = new List<byte>();
        for (var v = length; v > 0; v >>= 8)
        {
            bytes.Insert(0, (byte)(v & 0xFF));
        }

        bytes.Insert(0, (byte)(0x80 | bytes.Count));
        return [.. bytes];
    }
}
