namespace iConsole.Qr;

/// <summary>
/// Encodes bytes as a QR code (ISO/IEC 18004): byte mode, error correction level M, versions 1 to 10.
/// The result is the module matrix, indexed <c>[x, y]</c>, where <c>true</c> is a dark module. It has no
/// quiet zone; <see cref="QrTextRenderer"/> adds one. The encoder has no dependency; the tests check it
/// against published vectors and against a decoder.
/// </summary>
public static class QrEncoder
{
    public const int MaxVersion = 10;

    /// <summary>The longest input that fits a version 10 code at level M.</summary>
    public const int MaxBytes = 213;

    private const int LevelMIndicator = 0b00;
    private const int ByteModeIndicator = 0b0100;

    // Indexed by version (index 0 unused). The block layout at level M: group 1 blocks hold
    // Group1DataPerBlock data codewords, group 2 blocks hold one more.
    private static readonly int[] TotalCodewords = [0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346];
    private static readonly int[] EcCodewordsPerBlock = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
    private static readonly int[] Group1Blocks = [0, 1, 1, 1, 2, 2, 4, 4, 2, 3, 4];
    private static readonly int[] Group1DataPerBlock = [0, 16, 28, 44, 32, 43, 27, 31, 38, 36, 43];
    private static readonly int[] Group2Blocks = [0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 1];

    // Alignment pattern centres per version (index 0 and 1 have none).
    private static readonly int[][] AlignmentCenters =
    [
        [], [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50],
    ];

    private static readonly int[] GfExp = new int[512];
    private static readonly int[] GfLog = new int[256];

    static QrEncoder()
    {
        // GF(256) with the QR primitive polynomial 0x11D.
        int value = 1;
        for (int i = 0; i < 255; i++)
        {
            GfExp[i] = value;
            GfLog[value] = i;
            value <<= 1;
            if ((value & 0x100) != 0)
            {
                value ^= 0x11D;
            }
        }

        for (int i = 255; i < 512; i++)
        {
            GfExp[i] = GfExp[i - 255];
        }
    }

    /// <summary>The number of data bytes a version holds at level M, after the mode and length fields.</summary>
    public static int ByteCapacity(int version)
    {
        CheckVersion(version);
        return (DataCodewords(version) * 8 - 4 - CountBits(version)) / 8;
    }

    /// <summary>The smallest version whose level-M capacity holds <paramref name="byteCount"/> bytes.</summary>
    public static int VersionFor(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        for (int version = 1; version <= MaxVersion; version++)
        {
            if (byteCount <= ByteCapacity(version))
            {
                return version;
            }
        }

        throw new ArgumentException(
            $"{byteCount} bytes do not fit a version {MaxVersion} code at level M (at most {MaxBytes}).",
            nameof(byteCount));
    }

    /// <summary>Encodes the bytes as a QR code. Throws <see cref="ArgumentException"/> when they are too long.</summary>
    public static bool[,] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        int version = VersionFor(data.Length);
        byte[] codewords = DataCodewordsFor(data, version);
        byte[] symbolCodewords = Interleave(codewords, version);
        int size = SymbolSize(version);

        bool[,] baseModules = new bool[size, size];
        bool[,] isFunction = new bool[size, size];
        DrawFunctionPatterns(baseModules, isFunction, version, size);

        bool[,]? best = null;
        int bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            bool[,] modules = (bool[,])baseModules.Clone();
            PlaceData(modules, isFunction, symbolCodewords, mask, size);
            DrawFormat(modules, isFunction, FormatBits(mask), size);
            int penalty = Penalty(modules);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                best = modules;
            }
        }

        return best!;
    }

    /// <summary>
    /// The 15-bit format information for level M and a mask: the 5 data bits, the BCH(15,5) remainder,
    /// and the fixed mask 0x5412.
    /// </summary>
    public static int FormatBits(int mask)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mask, 7);
        ArgumentOutOfRangeException.ThrowIfNegative(mask);
        int data = (LevelMIndicator << 3) | mask;
        int remainder = data;
        for (int i = 0; i < 10; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        }

        return ((data << 10) | remainder) ^ 0x5412;
    }

    /// <summary>The 18-bit version information (versions 7 and up): the version and its BCH(18,6) remainder.</summary>
    public static int VersionBits(int version)
    {
        if (version < 7 || version > MaxVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Version information exists from version 7 to 10.");
        }

        int remainder = version;
        for (int i = 0; i < 12; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
        }

        return (version << 12) | remainder;
    }

    /// <summary>
    /// The Reed-Solomon check codewords of <paramref name="data"/> over GF(256), for <paramref name="degree"/>
    /// check codewords. Exposed so the tests can check the arithmetic on its own.
    /// </summary>
    public static byte[] ReedSolomonRemainder(byte[] data, int degree)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(degree);
        int[] generator = ReedSolomonGenerator(degree);
        int[] message = new int[data.Length + degree];
        for (int i = 0; i < data.Length; i++)
        {
            message[i] = data[i];
        }

        for (int i = 0; i < data.Length; i++)
        {
            int coefficient = message[i];
            if (coefficient != 0)
            {
                for (int j = 1; j <= degree; j++)
                {
                    message[i + j] ^= GfMultiply(generator[j], coefficient);
                }
            }
        }

        byte[] remainder = new byte[degree];
        for (int i = 0; i < degree; i++)
        {
            remainder[i] = (byte)message[data.Length + i];
        }

        return remainder;
    }

    /// <summary>The penalty score of a matrix (rules N1 to N4), which picks the mask.</summary>
    #pragma warning disable S2368 // A square module grid is the natural shape here; a jagged array would make the code harder to read.
    public static int Penalty(bool[,] modules)
    #pragma warning restore S2368
    {
        ArgumentNullException.ThrowIfNull(modules);
        int size = modules.GetLength(0);
        int score = 0;

        bool[] line = new bool[size];
        for (int a = 0; a < size; a++)
        {
            for (int b = 0; b < size; b++)
            {
                line[b] = modules[b, a];
            }

            score += LinePenalty(line);

            for (int b = 0; b < size; b++)
            {
                line[b] = modules[a, b];
            }

            score += LinePenalty(line);
        }

        for (int x = 0; x < size - 1; x++)
        {
            for (int y = 0; y < size - 1; y++)
            {
                bool colour = modules[x, y];
                if (colour == modules[x + 1, y] && colour == modules[x, y + 1] && colour == modules[x + 1, y + 1])
                {
                    score += 3;
                }
            }
        }

        int dark = 0;
        foreach (bool module in modules)
        {
            if (module)
            {
                dark++;
            }
        }

        int total = size * size;
        int deviation = Math.Abs(dark * 20 - total * 10);
        int steps = (deviation + total - 1) / total - 1;
        score += Math.Max(0, steps) * 10;

        return score;
    }

    private static int LinePenalty(bool[] line)
    {
        int score = 0;
        int run = 1;
        for (int i = 1; i <= line.Length; i++)
        {
            if (i < line.Length && line[i] == line[i - 1])
            {
                run++;
                continue;
            }

            if (run >= 5)
            {
                score += run - 2;
            }

            run = 1;
        }

        bool[] afterPattern = [true, false, true, true, true, false, true, false, false, false, false];
        bool[] beforePattern = [false, false, false, false, true, false, true, true, true, false, true];
        for (int i = 0; i + 11 <= line.Length; i++)
        {
            if (MatchesAt(line, i, afterPattern) || MatchesAt(line, i, beforePattern))
            {
                score += 40;
            }
        }

        return score;
    }

    private static bool MatchesAt(bool[] line, int start, bool[] pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (line[start + i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }

    private static void CheckVersion(int version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(version, MaxVersion);
    }

    private static int SymbolSize(int version) => 4 * version + 17;

    private static int CountBits(int version) => version <= 9 ? 8 : 16;

    private static int DataCodewords(int version) =>
        Group1Blocks[version] * Group1DataPerBlock[version]
        + Group2Blocks[version] * (Group1DataPerBlock[version] + 1);

    private static byte[] DataCodewordsFor(byte[] data, int version)
    {
        List<bool> bits = [];
        AppendBits(bits, ByteModeIndicator, 4);
        AppendBits(bits, data.Length, CountBits(version));
        foreach (byte value in data)
        {
            AppendBits(bits, value, 8);
        }

        int capacity = DataCodewords(version) * 8;
        AppendBits(bits, 0, Math.Min(4, capacity - bits.Count));
        while (bits.Count % 8 != 0)
        {
            bits.Add(false);
        }

        byte[] codewords = new byte[DataCodewords(version)];
        for (int i = 0; i < bits.Count / 8; i++)
        {
            int value = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value << 1) | (bits[i * 8 + bit] ? 1 : 0);
            }

            codewords[i] = (byte)value;
        }

        int pad = 0xEC;
        for (int i = bits.Count / 8; i < codewords.Length; i++)
        {
            codewords[i] = (byte)pad;
            pad = pad == 0xEC ? 0x11 : 0xEC;
        }

        return codewords;
    }

    private static void AppendBits(List<bool> bits, int value, int count)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) != 0);
        }
    }

    private static byte[] Interleave(byte[] data, int version)
    {
        int ecPerBlock = EcCodewordsPerBlock[version];
        int dataPerBlock = Group1DataPerBlock[version];
        List<byte[]> blocks = [];
        int offset = 0;
        for (int i = 0; i < Group1Blocks[version]; i++)
        {
            blocks.Add(data[offset..(offset + dataPerBlock)]);
            offset += dataPerBlock;
        }

        for (int i = 0; i < Group2Blocks[version]; i++)
        {
            blocks.Add(data[offset..(offset + dataPerBlock + 1)]);
            offset += dataPerBlock + 1;
        }

        List<byte[]> checks = blocks.Select(block => ReedSolomonRemainder(block, ecPerBlock)).ToList();
        List<byte> result = [];
        int longest = blocks.Max(block => block.Length);
        for (int i = 0; i < longest; i++)
        {
            foreach (byte[] block in blocks)
            {
                if (i < block.Length)
                {
                    result.Add(block[i]);
                }
            }
        }

        for (int i = 0; i < ecPerBlock; i++)
        {
            foreach (byte[] check in checks)
            {
                result.Add(check[i]);
            }
        }

        if (result.Count != TotalCodewords[version])
        {
            throw new InvalidOperationException($"Version {version} must have {TotalCodewords[version]} codewords, not {result.Count}.");
        }

        return result.ToArray();
    }

    private static int[] ReedSolomonGenerator(int degree)
    {
        int[] generator = [1];
        for (int i = 0; i < degree; i++)
        {
            int[] next = new int[generator.Length + 1];
            for (int j = 0; j < generator.Length; j++)
            {
                next[j] ^= generator[j];
                next[j + 1] ^= GfMultiply(generator[j], GfExp[i]);
            }

            generator = next;
        }

        return generator;
    }

    private static int GfMultiply(int a, int b) => a == 0 || b == 0 ? 0 : GfExp[GfLog[a] + GfLog[b]];

    private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction, int version, int size)
    {
        DrawFinder(modules, isFunction, size, 0, 0);
        DrawFinder(modules, isFunction, size, size - 7, 0);
        DrawFinder(modules, isFunction, size, 0, size - 7);

        int[] centers = AlignmentCenters[version];
        int last = centers.Length - 1;
        for (int i = 0; i < centers.Length; i++)
        {
            for (int j = 0; j < centers.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0))
                {
                    continue;
                }

                DrawAlignment(modules, isFunction, centers[i], centers[j]);
            }
        }

        for (int i = 0; i < size; i++)
        {
            if (!isFunction[6, i])
            {
                modules[6, i] = i % 2 == 0;
                isFunction[6, i] = true;
            }

            if (!isFunction[i, 6])
            {
                modules[i, 6] = i % 2 == 0;
                isFunction[i, 6] = true;
            }
        }

        DrawFormat(modules, isFunction, FormatBits(0), size);
        if (version >= 7)
        {
            int versionBits = VersionBits(version);
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((versionBits >> i) & 1) != 0;
                int a = size - 11 + i % 3;
                int b = i / 3;
                modules[a, b] = bit;
                isFunction[a, b] = true;
                modules[b, a] = bit;
                isFunction[b, a] = true;
            }
        }
    }

    private static void DrawFinder(bool[,] modules, bool[,] isFunction, int size, int left, int top)
    {
        for (int dy = -1; dy <= 7; dy++)
        {
            for (int dx = -1; dx <= 7; dx++)
            {
                int x = left + dx;
                int y = top + dy;
                if (x < 0 || y < 0 || x >= size || y >= size)
                {
                    continue;
                }

                int distance = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                modules[x, y] = distance != 2 && distance != 4;
                isFunction[x, y] = true;
            }
        }
    }

    private static void DrawAlignment(bool[,] modules, bool[,] isFunction, int centerX, int centerY)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                modules[centerX + dx, centerY + dy] = distance != 1;
                isFunction[centerX + dx, centerY + dy] = true;
            }
        }
    }

    private static void DrawFormat(bool[,] modules, bool[,] isFunction, int bits, int size)
    {
        void Set(int x, int y, bool value)
        {
            modules[x, y] = value;
            isFunction[x, y] = true;
        }

        static bool Bit(int value, int index) => ((value >> index) & 1) != 0;

        for (int i = 0; i <= 5; i++)
        {
            Set(8, i, Bit(bits, i));
        }

        Set(8, 7, Bit(bits, 6));
        Set(8, 8, Bit(bits, 7));
        Set(7, 8, Bit(bits, 8));
        for (int i = 9; i < 15; i++)
        {
            Set(14 - i, 8, Bit(bits, i));
        }

        for (int i = 0; i < 8; i++)
        {
            Set(size - 1 - i, 8, Bit(bits, i));
        }

        for (int i = 8; i < 15; i++)
        {
            Set(8, size - 15 + i, Bit(bits, i));
        }

        Set(8, size - 8, true);
    }

    private static void PlaceData(bool[,] modules, bool[,] isFunction, byte[] codewords, int mask, int size)
    {
        int totalBits = codewords.Length * 8;
        int index = 0;
        int right = size - 1;
        while (right >= 1)
        {
            int rightEdge = right == 6 ? 5 : right;
            for (int vertical = 0; vertical < size; vertical++)
            {
                for (int column = 0; column < 2; column++)
                {
                    int x = rightEdge - column;
                    bool upward = ((rightEdge + 1) & 2) == 0;
                    int y = upward ? size - 1 - vertical : vertical;
                    if (isFunction[x, y])
                    {
                        continue;
                    }

                    // Modules past the data are the remainder bits: they stay light before masking.
                    bool bit = index < totalBits && ((codewords[index >> 3] >> (7 - (index & 7))) & 1) != 0;
                    modules[x, y] = bit ^ MaskAt(mask, x, y);
                    index++;
                }
            }
            right = rightEdge - 2;
        }
    }

    private static bool MaskAt(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => (x / 3 + y / 2) % 2 == 0,
        5 => (x * y) % 2 + (x * y) % 3 == 0,
        6 => ((x * y) % 2 + (x * y) % 3) % 2 == 0,
        _ => ((x + y) % 2 + (x * y) % 3) % 2 == 0,
    };
}
