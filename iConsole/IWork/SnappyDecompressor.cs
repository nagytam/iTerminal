namespace iConsole.IWork;

/// <summary>
/// Decompresses one raw Snappy block, the compression iWork puts around each chunk of its .iwa archives (the format
/// described in the Snappy repository's format_description.txt). Written from that description; no Snappy library is used.
/// </summary>
internal static class SnappyDecompressor
{
    /// <summary>A larger block is not a Pages, Numbers or Keynote chunk, so it is refused rather than allocated.</summary>
    private const long MaxOutputLength = 256L * 1024 * 1024;

    public static byte[] Decompress(ReadOnlySpan<byte> input)
    {
        int position = 0;
        long length = ReadVarint(input, ref position);
        if (length > MaxOutputLength)
        {
            throw new InvalidDataException("a compressed block is too large");
        }

        byte[] output = new byte[length];
        int written = 0;
        while (position < input.Length)
        {
            byte tag = input[position++];
            switch (tag & 3)
            {
                case 0:
                {
                    int literal = tag >> 2;
                    if (literal >= 60)
                    {
                        int extra = literal - 59;
                        literal = 0;
                        for (int i = 0; i < extra; i++)
                        {
                            literal |= Byte(input, position++) << (8 * i);
                        }
                    }

                    literal += 1;
                    if (position + literal > input.Length || written + literal > output.Length)
                    {
                        throw new InvalidDataException("a literal runs past the end of a block");
                    }

                    input.Slice(position, literal).CopyTo(output.AsSpan(written));
                    position += literal;
                    written += literal;
                    break;
                }

                default:
                {
                    int copyLength;
                    int offset;
                    if ((tag & 3) == 1)
                    {
                        copyLength = ((tag >> 2) & 7) + 4;
                        offset = ((tag >> 5) << 8) | Byte(input, position++);
                    }
                    else if ((tag & 3) == 2)
                    {
                        copyLength = (tag >> 2) + 1;
                        offset = Byte(input, position) | (Byte(input, position + 1) << 8);
                        position += 2;
                    }
                    else
                    {
                        copyLength = (tag >> 2) + 1;
                        offset = Byte(input, position) | (Byte(input, position + 1) << 8)
                            | (Byte(input, position + 2) << 16) | (Byte(input, position + 3) << 24);
                        position += 4;
                    }

                    if (offset <= 0 || offset > written || written + copyLength > output.Length)
                    {
                        throw new InvalidDataException("a copy refers outside the block");
                    }

                    // A copy may overlap what it writes (offset smaller than length), so it is made a byte at a time.
                    for (int i = 0; i < copyLength; i++)
                    {
                        output[written] = output[written - offset];
                        written++;
                    }

                    break;
                }
            }
        }

        if (written != output.Length)
        {
            throw new InvalidDataException("a block is shorter than its header says");
        }

        return output;
    }

    private static int Byte(ReadOnlySpan<byte> input, int position)
    {
        if (position < 0 || position >= input.Length)
        {
            throw new InvalidDataException("a block ends in the middle of a tag");
        }

        return input[position];
    }

    private static long ReadVarint(ReadOnlySpan<byte> input, ref int position)
    {
        long value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            int b = Byte(input, position++);
            value |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("a block length is malformed");
    }
}
