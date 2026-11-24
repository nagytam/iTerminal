using System.Buffers.Binary;

namespace iConsole.Formats.Fonts;

/// <summary>
/// Rebuilds the two tables a WOFF 2.0 file stores in transformed form: <c>glyf</c> (with <c>loca</c> derived from it)
/// and <c>hmtx</c>. The transformed <c>glyf</c> keeps each glyph's contour counts, point counts, flags, coordinates,
/// composite records, bounding boxes and instructions in separate streams, with coordinates triplet-encoded. Each
/// glyph is written back in the TrueType form, four-byte aligned, so the offsets in <c>loca</c> are valid for both index formats.
/// </summary>
internal static class Woff2Transforms
{
    private const int GlyfHeaderLength = 36;
    private const int OverlapSimpleBitmapFlag = 0x0001;

    // The flag bits of a TrueType simple glyph, and the component flags of a composite glyph.
    private const byte OnCurve = 0x01;
    private const byte XShort = 0x02;
    private const byte YShort = 0x04;
    private const byte XSameOrPositive = 0x10;
    private const byte YSameOrPositive = 0x20;
    private const byte OverlapSimple = 0x40;
    private const int ArgsAreWords = 0x0001;
    private const int WeHaveAScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int WeHaveAnXAndYScale = 0x0040;
    private const int WeHaveATwoByTwo = 0x0080;
    private const int WeHaveInstructions = 0x0100;

    /// <summary>The rebuilt <c>glyf</c> and <c>loca</c> tables, the left bearing of every glyph (its xMin), and the loca index format.</summary>
    public sealed record GlyfResult(byte[] Glyf, byte[] Loca, short[] XMin, int IndexFormat);

    public static GlyfResult RebuildGlyf(byte[] data)
    {
        if (data.Length < GlyfHeaderLength)
        {
            throw new FormatException("the transformed glyf table is cut short");
        }

        var header = new ByteCursor(data);
        header.Skip(2); // version
        int optionFlags = header.ReadUInt16();
        int numGlyphs = header.ReadUInt16();
        int indexFormat = header.ReadUInt16();
        if (indexFormat > 1)
        {
            throw new FormatException("the glyf table has an unknown loca index format");
        }

        var sizes = new uint[7];
        for (int i = 0; i < sizes.Length; i++)
        {
            sizes[i] = header.ReadUInt32();
        }

        var body = new ByteCursor(data[GlyfHeaderLength..]);
        var nContour = TakeStream(body, sizes[0]);
        var nPoints = TakeStream(body, sizes[1]);
        var flags = TakeStream(body, sizes[2]);
        var glyphs = TakeStream(body, sizes[3]);
        var composites = TakeStream(body, sizes[4]);
        var bboxes = TakeStream(body, sizes[5]);
        var instructions = TakeStream(body, sizes[6]);
        byte[]? overlapBitmap = (optionFlags & OverlapSimpleBitmapFlag) != 0
            ? body.Take((numGlyphs + 7) >> 3)
            : null;
        if (body.Remaining != 0)
        {
            throw new FormatException("the transformed glyf table has the wrong size");
        }

        int bboxBitmapLength = ((numGlyphs + 31) >> 5) << 2;
        byte[] bboxBitmap = bboxes.Take(bboxBitmapLength);

        var glyf = new MemoryStream();
        var loca = new long[numGlyphs + 1];
        var xMin = new short[numGlyphs];
        for (int glyph = 0; glyph < numGlyphs; glyph++)
        {
            loca[glyph] = glyf.Length;
            short contours = nContour.ReadInt16();
            bool haveBox = (bboxBitmap[glyph >> 3] & (0x80 >> (glyph & 7))) != 0;

            if (contours == 0)
            {
                continue;
            }

            if (contours == -1)
            {
                WriteComposite(glyf, composites, glyphs, instructions, bboxes, haveBox, out xMin[glyph]);
            }
            else if (contours > 0)
            {
                WriteSimple(glyf, contours, glyph, nPoints, flags, glyphs, instructions, bboxes, haveBox, overlapBitmap, out xMin[glyph]);
            }
            else
            {
                throw new FormatException("a glyph has an invalid contour count");
            }

            PadTo4(glyf);
        }

        loca[numGlyphs] = glyf.Length;
        return new GlyfResult(glyf.ToArray(), BuildLoca(loca, indexFormat), xMin, indexFormat);
    }

    /// <summary>The <c>hmtx</c> table rebuilt from its transformed form, which omits the left bearings that equal each glyph's xMin.</summary>
    public static byte[] RebuildHmtx(byte[] data, int numberOfHMetrics, short[] xMin)
    {
        int numGlyphs = xMin.Length;
        int metrics = Math.Min(numberOfHMetrics, numGlyphs);
        var cursor = new ByteCursor(data);
        byte flags = cursor.ReadByte();
        if ((flags & 0xFC) != 0)
        {
            throw new FormatException("the transformed hmtx flags have reserved bits set");
        }

        bool hasLsbArray = (flags & 1) == 0;
        bool hasRightBearingArray = (flags & 2) == 0;
        if (hasLsbArray && hasRightBearingArray)
        {
            throw new FormatException("the transformed hmtx table omits no left bearings");
        }

        var advances = new ushort[metrics];
        for (int i = 0; i < metrics; i++)
        {
            advances[i] = cursor.ReadUInt16();
        }

        var lsb = new short[metrics];
        for (int i = 0; i < metrics; i++)
        {
            lsb[i] = hasLsbArray ? cursor.ReadInt16() : xMin[i];
        }

        var rightBearings = new short[numGlyphs - metrics];
        for (int i = 0; i < rightBearings.Length; i++)
        {
            rightBearings[i] = hasRightBearingArray ? cursor.ReadInt16() : xMin[metrics + i];
        }

        if (cursor.Remaining != 0)
        {
            throw new FormatException("the transformed hmtx table has too much data");
        }

        var output = new byte[metrics * 4 + rightBearings.Length * 2];
        for (int i = 0; i < metrics; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(i * 4), advances[i]);
            BinaryPrimitives.WriteInt16BigEndian(output.AsSpan(i * 4 + 2), lsb[i]);
        }

        for (int i = 0; i < rightBearings.Length; i++)
        {
            BinaryPrimitives.WriteInt16BigEndian(output.AsSpan(metrics * 4 + i * 2), rightBearings[i]);
        }

        return output;
    }

    private static ByteCursor TakeStream(ByteCursor body, uint size)
    {
        if (size > body.Remaining)
        {
            throw new FormatException("a glyf stream lies outside the table");
        }

        return new ByteCursor(body.Take((int)size));
    }

    private static void WriteSimple(
        MemoryStream glyf, short contours, int glyph, ByteCursor nPoints, ByteCursor flags, ByteCursor glyphs,
        ByteCursor instructions, ByteCursor bboxes, bool haveBox, byte[]? overlapBitmap, out short xMinOut)
    {
        var endPoints = new ushort[contours];
        int endPoint = -1;
        for (int contour = 0; contour < contours; contour++)
        {
            endPoint += nPoints.Read255UInt16();
            endPoints[contour] = (ushort)endPoint;
        }

        int pointCount = endPoint + 1;
        byte[] rawFlags = flags.Take(pointCount);
        var xs = new int[pointCount];
        var ys = new int[pointCount];
        var onCurve = new bool[pointCount];
        int x = 0, y = 0;
        for (int point = 0; point < pointCount; point++)
        {
            (int dx, int dy) = DecodeTriplet(rawFlags[point], glyphs, out int consumed);
            glyphs.Skip(consumed);
            x += dx;
            y += dy;
            xs[point] = x;
            ys[point] = y;
            onCurve[point] = (rawFlags[point] & 0x80) == 0;
        }

        int instructionLength = glyphs.Read255UInt16();
        byte[] program = instructions.Take(instructionLength);

        short xMin, yMin, xMax, yMax;
        if (haveBox)
        {
            xMin = bboxes.ReadInt16();
            yMin = bboxes.ReadInt16();
            xMax = bboxes.ReadInt16();
            yMax = bboxes.ReadInt16();
        }
        else
        {
            (xMin, yMin, xMax, yMax) = Bounds(xs, ys);
        }

        bool overlap = overlapBitmap is not null && (overlapBitmap[glyph >> 3] & (0x80 >> (glyph & 7))) != 0;

        WriteInt16(glyf, contours);
        WriteInt16(glyf, xMin);
        WriteInt16(glyf, yMin);
        WriteInt16(glyf, xMax);
        WriteInt16(glyf, yMax);
        foreach (ushort end in endPoints)
        {
            WriteUInt16(glyf, end);
        }

        WriteUInt16(glyf, (ushort)instructionLength);
        glyf.Write(program);

        // Each point gets one flag byte; a coordinate is one byte when its delta fits, two bytes otherwise, or none when it is zero.
        var flagBytes = new MemoryStream();
        var xBytes = new MemoryStream();
        var yBytes = new MemoryStream();
        int previousX = 0, previousY = 0;
        for (int point = 0; point < pointCount; point++)
        {
            byte flag = onCurve[point] ? OnCurve : (byte)0;
            if (point == 0 && overlap)
            {
                flag |= OverlapSimple;
            }

            flag |= EncodeAxis(xs[point] - previousX, XShort, XSameOrPositive, xBytes);
            flag |= EncodeAxis(ys[point] - previousY, YShort, YSameOrPositive, yBytes);
            flagBytes.WriteByte(flag);
            previousX = xs[point];
            previousY = ys[point];
        }

        flagBytes.WriteTo(glyf);
        xBytes.WriteTo(glyf);
        yBytes.WriteTo(glyf);
        xMinOut = xMin;
    }

    private static void WriteComposite(
        MemoryStream glyf, ByteCursor composites, ByteCursor glyphs, ByteCursor instructions, ByteCursor bboxes,
        bool haveBox, out short xMinOut)
    {
        if (!haveBox)
        {
            throw new FormatException("a composite glyph has no bounding box");
        }

        int componentsLength = CompositeLength(composites, out bool haveInstructions);
        byte[] components = composites.Take(componentsLength);
        short xMin = bboxes.ReadInt16();
        short yMin = bboxes.ReadInt16();
        short xMax = bboxes.ReadInt16();
        short yMax = bboxes.ReadInt16();

        WriteInt16(glyf, -1);
        WriteInt16(glyf, xMin);
        WriteInt16(glyf, yMin);
        WriteInt16(glyf, xMax);
        WriteInt16(glyf, yMax);
        glyf.Write(components);
        if (haveInstructions)
        {
            int instructionLength = glyphs.Read255UInt16();
            WriteUInt16(glyf, (ushort)instructionLength);
            glyf.Write(instructions.Take(instructionLength));
        }

        xMinOut = xMin;
    }

    /// <summary>The byte length of the components of one composite glyph, which end at the component without the more-components flag.</summary>
    private static int CompositeLength(ByteCursor composites, out bool haveInstructions)
    {
        int length = 0;
        haveInstructions = false;
        bool more = true;
        while (more)
        {
            int flags = composites.PeekUInt16(length);
            length += 4; // flags and glyph index
            length += (flags & ArgsAreWords) != 0 ? 4 : 2;
            if ((flags & WeHaveAScale) != 0)
            {
                length += 2;
            }
            else if ((flags & WeHaveAnXAndYScale) != 0)
            {
                length += 4;
            }
            else if ((flags & WeHaveATwoByTwo) != 0)
            {
                length += 8;
            }

            more = (flags & MoreComponents) != 0;
            haveInstructions |= (flags & WeHaveInstructions) != 0;
        }

        return length;
    }

    /// <summary>
    /// Reads one point's delta from the triplet encoding of the glyph stream (the table the WOFF 2.0 specification gives),
    /// returning the x and y deltas and the number of stream bytes the point used.
    /// </summary>
    private static (int Dx, int Dy) DecodeTriplet(byte rawFlag, ByteCursor glyphs, out int consumed)
    {
        int flag = rawFlag & 0x7F;
        consumed = flag switch { < 84 => 1, < 120 => 2, < 124 => 3, _ => 4 };
        if (consumed > glyphs.Remaining)
        {
            throw new FormatException("the glyph data is cut short");
        }

        int t0 = glyphs.Peek(0);
        int t1 = consumed > 1 ? glyphs.Peek(1) : 0;
        int t2 = consumed > 2 ? glyphs.Peek(2) : 0;
        int t3 = consumed > 3 ? glyphs.Peek(3) : 0;
        if (flag < 10)
        {
            return (0, WithSign(flag, ((flag & 14) << 7) + t0));
        }

        if (flag < 20)
        {
            return (WithSign(flag, (((flag - 10) & 14) << 7) + t0), 0);
        }

        if (flag < 84)
        {
            int b0 = flag - 20;
            int b1 = t0;
            return (
                WithSign(flag, 1 + (b0 & 0x30) + (b1 >> 4)),
                WithSign(flag >> 1, 1 + ((b0 & 0x0C) << 2) + (b1 & 0x0F)));
        }

        if (flag < 120)
        {
            int b0 = flag - 84;
            return (
                WithSign(flag, 1 + ((b0 / 12) << 8) + t0),
                WithSign(flag >> 1, 1 + (((b0 % 12) >> 2) << 8) + t1));
        }

        if (flag < 124)
        {
            int b2 = t1;
            return (
                WithSign(flag, (t0 << 4) + (b2 >> 4)),
                WithSign(flag >> 1, ((b2 & 0x0F) << 8) + t2));
        }

        return (
            WithSign(flag, (t0 << 8) + t1),
            WithSign(flag >> 1, (t2 << 8) + t3));
    }

    /// <summary>The sign of a triplet delta is its low bit: set for positive.</summary>
    private static int WithSign(int flag, int magnitude) => (flag & 1) != 0 ? magnitude : -magnitude;

    /// <summary>Writes one axis's delta in the TrueType form and returns the flag bits it needs.</summary>
    private static byte EncodeAxis(int delta, byte shortFlag, byte sameOrPositive, MemoryStream bytes)
    {
        if (delta == 0)
        {
            return sameOrPositive;
        }

        if (Math.Abs(delta) <= byte.MaxValue)
        {
            bytes.WriteByte((byte)Math.Abs(delta));
            return (byte)(shortFlag | (delta > 0 ? sameOrPositive : 0));
        }

        if (delta < short.MinValue || delta > short.MaxValue)
        {
            throw new FormatException("a glyph coordinate is out of range");
        }

        WriteInt16(bytes, (short)delta);
        return 0;
    }

    private static (short XMin, short YMin, short XMax, short YMax) Bounds(int[] xs, int[] ys)
    {
        if (xs.Length == 0)
        {
            return (0, 0, 0, 0);
        }

        int xMin = xs.Min(), yMin = ys.Min(), xMax = xs.Max(), yMax = ys.Max();
        return ((short)xMin, (short)yMin, (short)xMax, (short)yMax);
    }

    private static byte[] BuildLoca(long[] offsets, int indexFormat)
    {
        if (indexFormat == 0)
        {
            var shortLoca = new byte[offsets.Length * 2];
            for (int i = 0; i < offsets.Length; i++)
            {
                if (offsets[i] > 0x1FFFE)
                {
                    throw new FormatException("the glyf table is too large for short loca offsets");
                }

                BinaryPrimitives.WriteUInt16BigEndian(shortLoca.AsSpan(i * 2), (ushort)(offsets[i] / 2));
            }

            return shortLoca;
        }

        var longLoca = new byte[offsets.Length * 4];
        for (int i = 0; i < offsets.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(longLoca.AsSpan(i * 4), (uint)offsets[i]);
        }

        return longLoca;
    }

    private static void PadTo4(MemoryStream stream)
    {
        while (stream.Length % 4 != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void WriteInt16(Stream stream, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
