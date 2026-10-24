using System.Buffers.Binary;
using System.Text;
using iTerminal.Core.Services;

namespace iTerminal.Core.Services.Images;

/// <summary>The text fields an image's metadata editor sets. An empty field removes that text from the file.</summary>
public sealed record ImageMetadataEdit(string Description, string Artist, string Copyright, string Comment)
{
    public static readonly ImageMetadataEdit Empty = new(string.Empty, string.Empty, string.Empty, string.Empty);

    /// <summary>
    /// The text an image already has, in the four fields: an EXIF tag, or the PNG text chunk of the same meaning (the author
    /// is the artist). The first field found for each wins.
    /// </summary>
    public static ImageMetadataEdit From(IEnumerable<ImageInfoField> fields)
    {
        List<ImageInfoField> all = fields.ToList();
        string Find(params (string Group, string Name)[] keys) =>
            keys.Select(k => all.FirstOrDefault(f => f.Group == k.Group && f.Name == k.Name)?.Value).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty;

        return new ImageMetadataEdit(
            Find(("Image", "Description"), ("Text", "Description")),
            Find(("Image", "Artist"), ("Text", "Author")),
            Find(("Image", "Copyright"), ("Text", "Copyright")),
            Find(("Text", "Comment")));
    }
}

/// <summary>
/// Writes the text of a JPEG or a PNG. In a JPEG the description, artist and copyright are the EXIF text tags of the first
/// image directory, and the comment is the COM segment. In a PNG they are the <c>Description</c>, <c>Author</c>,
/// <c>Copyright</c> and <c>Comment</c> text chunks. Everything else is kept: the picture, the other EXIF tags (camera,
/// exposure, GPS) and the other chunks. The file is rebuilt in memory and replaced atomically.
/// </summary>
public static class ImageMetadataWriter
{
    private const ushort TagImageDescription = 0x010E;
    private const ushort TagArtist = 0x013B;
    private const ushort TagCopyright = 0x8298;
    private const ushort TagExifIfd = 0x8769;
    private const ushort TagGpsIfd = 0x8825;
    private const ushort TagInteropIfd = 0xA005;
    private const int MaxSegment = 0xFFFF;

    /// <summary>True for a JPEG or a PNG: the two formats whose text is written here.</summary>
    public static bool CanWrite(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes the edit into the image's text. Throws <see cref="NotSupportedException"/> for other formats and <see cref="InvalidDataException"/> for a damaged file.</summary>
    public static void Write(string path, ImageMetadataEdit edit)
    {
        byte[] content = File.ReadAllBytes(path);
        byte[] result;
        if (content.AsSpan().StartsWith(PngSignature))
        {
            result = WritePng(content, edit);
        }
        else if (content.Length > 2 && content[0] == 0xFF && content[1] == 0xD8)
        {
            result = WriteJpeg(content, edit);
        }
        else
        {
            throw new NotSupportedException("The text of an image can be written for JPEG and PNG files.");
        }

        FileReplacement.Replace(path, result);
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    #region JPEG

    private sealed record Segment(byte Marker, byte[] Payload);

    /// <summary>The segments of a JPEG up to its image data, which starts at <paramref name="scanStart"/>.</summary>
    private static List<Segment> SplitJpeg(byte[] content, out int scanStart)
    {
        // The image data is copied as it is, so only the segments before it are read.
        List<Segment> segments = [];
        int position = 2;
        scanStart = content.Length;
        while (position + 4 <= content.Length && content[position] == 0xFF)
        {
            byte marker = content[position + 1];
            if (marker == 0xDA)
            {
                scanStart = position;
                break;
            }

            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7 || marker == 0xFF)
            {
                position += marker == 0xFF ? 1 : 2;
                continue;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(position + 2));
            if (length < 2 || position + 2 + length > content.Length)
            {
                throw new InvalidDataException("A JPEG segment runs past the end of the file.");
            }

            segments.Add(new Segment(marker, content[(position + 4)..(position + 2 + length)]));
            position += 2 + length;
        }

        if (scanStart == content.Length)
        {
            throw new InvalidDataException("The JPEG has no image data.");
        }

        return segments;
    }

    private static byte[] WriteJpeg(byte[] content, ImageMetadataEdit edit)
    {
        List<Segment> segments = SplitJpeg(content, out int scanStart);
        bool isExif(Segment s) => s.Marker == 0xE1 && s.Payload.AsSpan().StartsWith("Exif\0\0"u8);
        byte[]? exifTiff = segments.FirstOrDefault(isExif) is { } exif ? exif.Payload[6..] : null;
        byte[]? newTiff = BuildExifTiff(exifTiff, edit);

        List<Segment> output = [];
        bool placedExif = false;
        bool placedComment = false;
        foreach (Segment segment in segments)
        {
            if (isExif(segment))
            {
                if (newTiff is not null && !placedExif)
                {
                    output.Add(new Segment(0xE1, [.. "Exif\0\0"u8, .. newTiff]));
                }

                placedExif = true;
            }
            else if (segment.Marker == 0xFE)
            {
                if (edit.Comment.Length > 0 && !placedComment)
                {
                    output.Add(new Segment(0xFE, Encoding.UTF8.GetBytes(edit.Comment)));
                }

                placedComment = true;
            }
            else
            {
                output.Add(segment);
            }
        }

        // Metadata the file did not have goes after the last application segment, or straight after the start of the image.
        if (!placedExif && newTiff is not null)
        {
            output.Insert(LastApplicationIndex(output) + 1, new Segment(0xE1, [.. "Exif\0\0"u8, .. newTiff]));
        }

        if (!placedComment && edit.Comment.Length > 0)
        {
            output.Add(new Segment(0xFE, Encoding.UTF8.GetBytes(edit.Comment)));
        }

        return JoinJpeg(output, content, scanStart);
    }

    /// <summary>The segments, in order, then the image data from <paramref name="scanStart"/> on as it was.</summary>
    private static byte[] JoinJpeg(List<Segment> segments, byte[] content, int scanStart)
    {
        using MemoryStream stream = new();
        stream.Write([0xFF, 0xD8]);
        foreach (Segment segment in segments)
        {
            if (segment.Payload.Length + 2 > MaxSegment)
            {
                throw new InvalidDataException("The metadata is too large for one JPEG segment.");
            }

            stream.Write([0xFF, segment.Marker, (byte)((segment.Payload.Length + 2) >> 8), (byte)(segment.Payload.Length + 2)]);
            stream.Write(segment.Payload);
        }

        stream.Write(content.AsSpan(scanStart));
        return stream.ToArray();
    }

    /// <summary>
    /// Removes what describes the picture rather than shows it: in a JPEG the EXIF, XMP and IPTC blocks and the comments;
    /// in a PNG the text chunks and the EXIF chunk. The picture, its colour profile and the layout data are kept. A file
    /// with nothing to remove is left alone; the return says whether it was replaced.
    /// </summary>
    public static bool Strip(string path)
    {
        byte[] content = File.ReadAllBytes(path);
        byte[] result;
        if (content.AsSpan().StartsWith(PngSignature))
        {
            result = StripPng(content);
        }
        else if (content.Length > 2 && content[0] == 0xFF && content[1] == 0xD8)
        {
            result = StripJpeg(content);
        }
        else
        {
            throw new NotSupportedException("Metadata can be stripped from JPEG and PNG files.");
        }

        if (result.AsSpan().SequenceEqual(content))
        {
            return false;
        }

        FileReplacement.Replace(path, result);
        return true;
    }

    /// <summary>The JFIF header, the ICC colour profile and the Adobe colour marker are what the picture needs to look right.</summary>
    private static byte[] StripJpeg(byte[] content)
    {
        List<Segment> segments = SplitJpeg(content, out int scanStart);
        List<Segment> kept = segments.Where(s => s.Marker != 0xFE && (s.Marker is < 0xE1 or > 0xEF || IsLayoutSegment(s))).ToList();
        return JoinJpeg(kept, content, scanStart);
    }

    private static bool IsLayoutSegment(Segment segment) =>
        segment.Marker == 0xE0
        || segment.Marker == 0xEE
        || (segment.Marker == 0xE2 && segment.Payload.AsSpan().StartsWith("ICC_PROFILE\0"u8));

    private static int LastApplicationIndex(List<Segment> segments)
    {
        int last = -1;
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].Marker is >= 0xE0 and <= 0xEF)
            {
                last = i;
            }
        }

        return last;
    }

    /// <summary>
    /// The EXIF block with the three text tags of the first image directory set to the edit (or removed when empty). The
    /// rest of the block, including the camera and GPS directories, is rebuilt from what was read, so it stays the same.
    /// </summary>
    private static byte[]? BuildExifTiff(byte[]? existing, ImageMetadataEdit edit)
    {
        TiffDirectory? root = existing is null ? null : TiffDirectory.Read(existing);
        root ??= TiffDirectory.Empty(bigEndian: false);
        root.SetText(TagImageDescription, edit.Description);
        root.SetText(TagArtist, edit.Artist);
        root.SetText(TagCopyright, edit.Copyright);
        if (root.Entries.Count == 0)
        {
            return null;
        }

        return root.Serialize();
    }

    /// <summary>One image file directory of a TIFF block: its entries, with the child directories the pointer entries name.</summary>
    private sealed class TiffDirectory
    {
        private readonly bool _bigEndian;

        private TiffDirectory(bool bigEndian)
        {
            _bigEndian = bigEndian;
        }

        public List<TiffEntry> Entries { get; } = [];

        public static TiffDirectory Empty(bool bigEndian) => new(bigEndian);

        public static TiffDirectory Read(byte[] tiff)
        {
            if (tiff.Length < 8)
            {
                return new TiffDirectory(false);
            }

            bool big = tiff[0] == 'M';
            return ReadDirectory(tiff, big, (int)ReadUInt32(tiff.AsSpan(4), big), 0);
        }

        private static TiffDirectory ReadDirectory(byte[] tiff, bool big, int offset, int depth)
        {
            TiffDirectory directory = new(big);
            if (offset <= 0 || depth > 4 || offset + 2 > tiff.Length)
            {
                return directory;
            }

            int count = ReadUInt16(tiff.AsSpan(offset), big);
            for (int i = 0; i < count; i++)
            {
                int at = offset + 2 + i * 12;
                if (at + 12 > tiff.Length)
                {
                    break;
                }

                ushort tag = ReadUInt16(tiff.AsSpan(at), big);
                ushort type = ReadUInt16(tiff.AsSpan(at + 2), big);
                uint count2 = ReadUInt32(tiff.AsSpan(at + 4), big);
                int size = TypeSize(type) * (int)count2;
                if (size <= 0 || size > tiff.Length)
                {
                    continue;
                }

                byte[] value = size <= 4
                    ? tiff.AsSpan(at + 8, size).ToArray()
                    : OutOfLine(tiff, (int)ReadUInt32(tiff.AsSpan(at + 8), big), size);
                TiffDirectory? child = null;
                if (IsPointer(tag) && type == 4 && count2 == 1)
                {
                    child = ReadDirectory(tiff, big, (int)ReadUInt32(value, big), depth + 1);
                }

                directory.Entries.Add(new TiffEntry(tag, type, count2, value, child));
            }

            return directory;
        }

        private static byte[] OutOfLine(byte[] tiff, int offset, int size) =>
            offset >= 0 && offset + size <= tiff.Length ? tiff.AsSpan(offset, size).ToArray() : new byte[size];

        private static bool IsPointer(ushort tag) => tag is TagExifIfd or TagGpsIfd or TagInteropIfd;

        /// <summary>Sets a text tag: an ASCII value with its terminator, or no tag at all for an empty text.</summary>
        public void SetText(ushort tag, string text)
        {
            Entries.RemoveAll(e => e.Tag == tag);
            if (text.Length == 0)
            {
                return;
            }

            byte[] bytes = [.. Encoding.UTF8.GetBytes(text), 0];
            Entries.Add(new TiffEntry(tag, 2, (uint)bytes.Length, bytes, null));
            Entries.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        }

        /// <summary>
        /// The block: a header, then each directory in turn, then the values too large for an entry. The pointers to child
        /// directories are rewritten to where those directories now are.
        /// </summary>
        public byte[] Serialize()
        {
            List<TiffDirectory> directories = [];
            Collect(this, directories);

            // Directory positions: after the 8-byte header, each directory is its count, its entries and the next-directory slot.
            Dictionary<TiffDirectory, int> at = [];
            int position = 8;
            foreach (TiffDirectory directory in directories)
            {
                at[directory] = position;
                position += 2 + directory.Entries.Count * 12 + 4;
            }

            int dataStart = position;
            using MemoryStream data = new();
            Dictionary<TiffEntry, int> valueOffsets = [];
            foreach (TiffDirectory directory in directories)
            {
                foreach (TiffEntry entry in directory.Entries.Where(e => e.Value.Length > 4 && e.Child is null))
                {
                    valueOffsets[entry] = dataStart + (int)data.Length;
                    data.Write(entry.Value);
                    if (data.Length % 2 == 1)
                    {
                        data.WriteByte(0);
                    }
                }
            }

            byte[] output = new byte[dataStart + data.Length];
            output[0] = (byte)(_bigEndian ? 'M' : 'I');
            output[1] = output[0];
            WriteUInt16(output.AsSpan(2), 42, _bigEndian);
            WriteUInt32(output.AsSpan(4), 8, _bigEndian);
            foreach (TiffDirectory directory in directories)
            {
                int start = at[directory];
                WriteUInt16(output.AsSpan(start), (ushort)directory.Entries.Count, _bigEndian);
                for (int i = 0; i < directory.Entries.Count; i++)
                {
                    TiffEntry entry = directory.Entries[i];
                    int slot = start + 2 + i * 12;
                    WriteUInt16(output.AsSpan(slot), entry.Tag, _bigEndian);
                    WriteUInt16(output.AsSpan(slot + 2), entry.Type, _bigEndian);
                    WriteUInt32(output.AsSpan(slot + 4), entry.Count, _bigEndian);
                    if (entry.Child is not null)
                    {
                        WriteUInt32(output.AsSpan(slot + 8), (uint)at[entry.Child], _bigEndian);
                    }
                    else if (entry.Value.Length <= 4)
                    {
                        entry.Value.CopyTo(output.AsSpan(slot + 8));
                    }
                    else
                    {
                        WriteUInt32(output.AsSpan(slot + 8), (uint)valueOffsets[entry], _bigEndian);
                    }
                }

                WriteUInt32(output.AsSpan(start + 2 + directory.Entries.Count * 12), 0, _bigEndian);
            }

            data.ToArray().CopyTo(output, dataStart);
            return output;
        }

        private static void Collect(TiffDirectory directory, List<TiffDirectory> into)
        {
            into.Add(directory);
            foreach (TiffEntry entry in directory.Entries.Where(e => e.Child is not null))
            {
                Collect(entry.Child!, into);
            }
        }
    }

    private sealed record TiffEntry(ushort Tag, ushort Type, uint Count, byte[] Value, TiffDirectory? Child);

    #endregion

    #region PNG

    /// <summary>The text chunks a PNG editor sets, by the keyword each is written under.</summary>
    private static readonly (string Keyword, Func<ImageMetadataEdit, string> Value)[] PngKeywords =
    [
        ("Description", e => e.Description),
        ("Author", e => e.Artist),
        ("Copyright", e => e.Copyright),
        ("Comment", e => e.Comment),
    ];

    private static byte[] WritePng(byte[] content, ImageMetadataEdit edit)
    {
        HashSet<string> keywords = PngKeywords.Select(k => k.Keyword).ToHashSet(StringComparer.Ordinal);
        return RewritePng(
            content,
            (type, data) => !(type is "tEXt" or "iTXt" && TextKeyword(data) is { } keyword && keywords.Contains(keyword)),
            output => WriteTextChunks(output, edit));
    }

    /// <summary>Decides, chunk by chunk, what a PNG keeps: the type and the data of each chunk.</summary>
    private delegate bool PngChunkFilter(string type, ReadOnlySpan<byte> data);

    /// <summary>
    /// Copies a PNG chunk by chunk, leaving out the ones <paramref name="keep"/> refuses. <paramref name="beforeImageData"/>,
    /// when given, writes ahead of the first image data chunk. Throws for a PNG without image data.
    /// </summary>
    private static byte[] RewritePng(byte[] content, PngChunkFilter keep, Action<Stream>? beforeImageData)
    {
        using MemoryStream output = new();
        output.Write(content, 0, PngSignature.Length);
        int position = PngSignature.Length;
        bool sawImageData = false;
        while (position + 12 <= content.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(content.AsSpan(position));
            string type = Encoding.ASCII.GetString(content, position + 4, 4);
            int total = 12 + (int)length;
            if (position + total > content.Length)
            {
                throw new InvalidDataException("A PNG chunk runs past the end of the file.");
            }

            if (type == "IDAT" && !sawImageData)
            {
                sawImageData = true;
                beforeImageData?.Invoke(output);
            }

            if (keep(type, content.AsSpan(position + 8, (int)length)))
            {
                output.Write(content, position, total);
            }

            position += total;
            if (type == "IEND")
            {
                break;
            }
        }

        if (!sawImageData)
        {
            throw new InvalidDataException("The PNG has no image data.");
        }

        return output.ToArray();
    }

    /// <summary>The PNG with its text chunks and its EXIF chunk left out.</summary>
    private static byte[] StripPng(byte[] content) =>
        RewritePng(content, (type, _) => type is not ("tEXt" or "zTXt" or "iTXt" or "eXIf" or "tIME"), beforeImageData: null);

    private static string? TextKeyword(ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf((byte)0);
        return end > 0 ? Encoding.Latin1.GetString(data[..end]) : null;
    }

    /// <summary>Writes the non-empty text chunks, before the image data. A text that is not Latin-1 goes in an iTXt chunk as UTF-8.</summary>
    private static void WriteTextChunks(Stream output, ImageMetadataEdit edit)
    {
        foreach ((string keyword, Func<ImageMetadataEdit, string> value) in PngKeywords)
        {
            string text = value(edit);
            if (text.Length == 0)
            {
                continue;
            }

            byte[] keywordBytes = Encoding.Latin1.GetBytes(keyword);
            if (text.All(c => c < 256))
            {
                WriteChunk(output, "tEXt", [.. keywordBytes, 0, .. Encoding.Latin1.GetBytes(text)]);
            }
            else
            {
                WriteChunk(output, "iTXt", [.. keywordBytes, 0, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes(text)]);
            }
        }
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        byte[] length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        output.Write(typeBytes);
        output.Write(data);
        byte[] crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes, data));
        output.Write(crc);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    /// <summary>The CRC-32 a PNG chunk carries, over its type and its data.</summary>
    internal static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFF;
    }

    #endregion

    private static int TypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 => 4,
        5 or 10 or 12 => 8,
        _ => 0,
    };

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool big) =>
        big ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool big) =>
        big ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);

    private static void WriteUInt16(Span<byte> bytes, ushort value, bool big)
    {
        if (big)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        }
    }

    private static void WriteUInt32(Span<byte> bytes, uint value, bool big)
    {
        if (big)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        }
    }
}
