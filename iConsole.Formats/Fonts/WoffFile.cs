using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace iConsole.Formats.Fonts;

/// <summary>
/// Unwraps a WOFF 1.0 web font into the TrueType or OpenType (sfnt) bytes it carries, so <see cref="FontFileReader"/>
/// and the preview read it like any installed font. Each table is zlib-compressed when that made it smaller, and is
/// stored as it is otherwise. The metadata and private blocks are not needed for the summary and are skipped.
/// WOFF 2.0 files are handed to <see cref="Woff2File"/>.
/// </summary>
public static class WoffFile
{
    private const uint Signature = 0x774F4646; // 'wOFF'
    private const int HeaderLength = 44;
    private const int EntryLength = 20;
    private const long MaxTableLength = 64L * 1024 * 1024;

    /// <summary>Whether the bytes begin with the WOFF 1.0 signature.</summary>
    public static bool IsWoff(byte[] bytes) =>
        bytes.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(bytes) == Signature;

    /// <summary>The bytes unchanged when they are not a WOFF file of either version, otherwise the sfnt font they wrap.</summary>
    public static byte[] Unwrap(byte[] bytes)
    {
        if (IsWoff(bytes)) return ToSfnt(bytes);
        if (Woff2File.IsWoff2(bytes)) return Woff2File.ToSfnt(bytes);
        return bytes;
    }

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

        var tables = new List<(string Tag, byte[] Data)>(count);
        for (int i = 0; i < count; i++)
        {
            int entry = HeaderLength + i * EntryLength;
            string tag = Encoding.Latin1.GetString(bytes, entry, 4);
            long offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry + 4));
            long compressed = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry + 8));
            long original = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(entry + 12));
            if (original > MaxTableLength || compressed > original || offset + compressed > bytes.Length)
            {
                throw new FormatException("a WOFF table lies outside the file");
            }

            tables.Add((tag, TableData(bytes, (int)offset, (int)compressed, (int)original)));
        }

        return SfntBuilder.Build(flavor, tables);
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
}
