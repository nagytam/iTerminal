using System.Text;

namespace iTerminal.Core.Documents.Extractors;

/// <summary>
/// The text an image carries about itself: PNG text chunks, JPEG comments, EXIF descriptions and
/// XMP. Read directly from the bytes, because every one of these is a short, well-specified
/// structure near the front of the file and none of them needs a decoder — nothing here ever looks
/// at a pixel.
///
/// <para>This is what makes image search work at all on a machine with no OCR engine, and it is
/// often the better answer even on one that has: a photograph captioned by its owner has a caption
/// worth more than anything OCR would find in it.</para>
/// </summary>
internal static class ImageMetadataReader
{
    /// <summary>
    /// Metadata lives at the front of these formats, so a prefix is all that has to be read. A
    /// 40 MB scan would otherwise be loaded in full to find a caption in its first kilobyte.
    /// </summary>
    private const int MaxHeaderBytes = 512 * 1024;

    /// <summary>What one image had to say about itself.</summary>
    internal sealed record ImageMetadata(string Title, string Author, string Description)
    {
        internal static readonly ImageMetadata Empty = new(string.Empty, string.Empty, string.Empty);

        internal bool IsEmpty => Title.Length == 0 && Author.Length == 0 && Description.Length == 0;

        /// <summary>The parts that are not empty, as one block of text for the index.</summary>
        internal string ToText() => string.Join(
            '\n',
            new[] { Title, Author, Description }.Where(part => part.Length > 0));
    }

    internal static ImageMetadata Read(string path)
    {
        try
        {
            byte[] bytes = ReadHeader(path);

            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
            {
                return ReadPng(bytes);
            }

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                return ReadJpeg(bytes);
            }

            return ImageMetadata.Empty;
        }
        catch
        {
            // Metadata is a bonus on top of the file name; a malformed header costs only itself.
            return ImageMetadata.Empty;
        }
    }

    private static byte[] ReadHeader(string path)
    {
        using FileStream stream = File.OpenRead(path);
        int length = (int)Math.Min(stream.Length, MaxHeaderBytes);
        byte[] buffer = new byte[length];
        stream.ReadExactly(buffer, 0, length);
        return buffer;
    }

    #region PNG

    /// <summary>
    /// PNG keeps its text in <c>tEXt</c>, <c>zTXt</c> and <c>iTXt</c> chunks, each a keyword and a
    /// value. The keywords are conventional names — "Title", "Author", "Description", "Comment" —
    /// so the chunk walk is the whole of it.
    /// </summary>
    private static ImageMetadata ReadPng(byte[] bytes)
    {
        string title = string.Empty;
        string author = string.Empty;
        List<string> descriptions = [];

        int offset = 8; // the signature

        while (offset + 8 <= bytes.Length)
        {
            int length = ReadBigEndianInt32(bytes, offset);
            if (length < 0 || length > bytes.Length - offset - 12)
            {
                break;
            }

            string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            int dataOffset = offset + 8;

            if (type is "tEXt" or "zTXt" or "iTXt")
            {
                (string Keyword, string Value)? entry = type switch
                {
                    "tEXt" => ReadPngText(bytes, dataOffset, length),
                    "zTXt" => ReadPngCompressedText(bytes, dataOffset, length),
                    _ => ReadPngInternationalText(bytes, dataOffset, length),
                };

                if (entry is { } text && text.Value.Trim().Length > 0)
                {
                    switch (text.Keyword.ToLowerInvariant())
                    {
                        case "title":
                            title = title.Length == 0 ? text.Value.Trim() : title;
                            break;

                        case "author":
                        case "artist":
                            author = author.Length == 0 ? text.Value.Trim() : author;
                            break;

                        default:
                            // Keeping the keyword makes the preview readable: "Software: GIMP" says
                            // more than a bare "GIMP" in a list of unlabelled strings.
                            descriptions.Add($"{text.Keyword}: {text.Value.Trim()}");
                            break;
                    }
                }
            }

            if (type == "IDAT" || type == "IEND")
            {
                // Text chunks are allowed after the pixels, but a scan that has reached them has
                // already read everything that is conventionally there, and the rest of the file is
                // megabytes of compressed image.
                break;
            }

            offset += 12 + length;
        }

        return new ImageMetadata(title, author, string.Join('\n', descriptions));
    }

    private static (string Keyword, string Value)? ReadPngText(byte[] bytes, int offset, int length)
    {
        int separator = Array.IndexOf(bytes, (byte)0, offset, length);
        if (separator < 0)
        {
            return null;
        }

        string keyword = Encoding.Latin1.GetString(bytes, offset, separator - offset);
        string value = Encoding.Latin1.GetString(bytes, separator + 1, offset + length - separator - 1);
        return (keyword, value);
    }

    private static (string Keyword, string Value)? ReadPngCompressedText(byte[] bytes, int offset, int length)
    {
        int separator = Array.IndexOf(bytes, (byte)0, offset, length);
        if (separator < 0 || separator + 2 > offset + length)
        {
            return null;
        }

        string keyword = Encoding.Latin1.GetString(bytes, offset, separator - offset);

        // separator + 1 is the compression method byte; the zlib stream starts after it.
        int dataOffset = separator + 2;
        int dataLength = offset + length - dataOffset;
        if (dataLength <= 0)
        {
            return null;
        }

        try
        {
            using MemoryStream input = new(bytes, dataOffset, dataLength);
            using System.IO.Compression.ZLibStream decompressor = new(input, System.IO.Compression.CompressionMode.Decompress);
            using MemoryStream output = new();
            decompressor.CopyTo(output);
            return (keyword, Encoding.Latin1.GetString(output.ToArray()));
        }
        catch
        {
            return null;
        }
    }

    private static (string Keyword, string Value)? ReadPngInternationalText(byte[] bytes, int offset, int length)
    {
        int end = offset + length;
        int separator = Array.IndexOf(bytes, (byte)0, offset, length);
        if (separator < 0 || separator + 2 >= end)
        {
            return null;
        }

        string keyword = Encoding.Latin1.GetString(bytes, offset, separator - offset);
        byte compressionFlag = bytes[separator + 1];

        // Then: compression method, language tag (null-terminated), translated keyword
        // (null-terminated), and finally the text — which is UTF-8, unlike every other PNG string.
        int cursor = separator + 3;
        for (int skipped = 0; skipped < 2 && cursor < end; skipped++)
        {
            int terminator = Array.IndexOf(bytes, (byte)0, cursor, end - cursor);
            if (terminator < 0)
            {
                return null;
            }

            cursor = terminator + 1;
        }

        if (cursor >= end)
        {
            return null;
        }

        if (compressionFlag == 0)
        {
            return (keyword, Encoding.UTF8.GetString(bytes, cursor, end - cursor));
        }

        try
        {
            using MemoryStream input = new(bytes, cursor, end - cursor);
            using System.IO.Compression.ZLibStream decompressor = new(input, System.IO.Compression.CompressionMode.Decompress);
            using MemoryStream output = new();
            decompressor.CopyTo(output);
            return (keyword, Encoding.UTF8.GetString(output.ToArray()));
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region JPEG

    /// <summary>
    /// Walks the JPEG marker segments looking for the three that can carry text: <c>COM</c>, and the
    /// two shapes of <c>APP1</c> — EXIF and XMP. The walk stops at the start of scan, after which
    /// the file is entropy-coded image data with no more structure to read.
    /// </summary>
    private static ImageMetadata ReadJpeg(byte[] bytes)
    {
        string title = string.Empty;
        string author = string.Empty;
        List<string> descriptions = [];

        int offset = 2; // the SOI marker

        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                break;
            }

            byte marker = bytes[offset + 1];

            // Start of scan, or end of image: everything after is compressed pixels.
            if (marker == 0xDA || marker == 0xD9)
            {
                break;
            }

            int segmentLength = (bytes[offset + 2] << 8) | bytes[offset + 3];
            if (segmentLength < 2 || offset + 2 + segmentLength > bytes.Length)
            {
                break;
            }

            int dataOffset = offset + 4;
            int dataLength = segmentLength - 2;

            if (marker == 0xFE)
            {
                string comment = Encoding.Latin1.GetString(bytes, dataOffset, dataLength).Trim();
                if (comment.Length > 0)
                {
                    descriptions.Add(comment);
                }
            }
            else if (marker == 0xE1)
            {
                if (dataLength > 6 && Encoding.ASCII.GetString(bytes, dataOffset, 4) == "Exif")
                {
                    ExifStrings exif = ReadExif(bytes, dataOffset + 6, dataLength - 6);
                    title = title.Length == 0 ? exif.Title : title;
                    author = author.Length == 0 ? exif.Artist : author;
                    if (exif.Description.Length > 0)
                    {
                        descriptions.Add(exif.Description);
                    }
                }
                else if (dataLength > 28)
                {
                    string header = Encoding.ASCII.GetString(bytes, dataOffset, Math.Min(28, dataLength));
                    if (header.StartsWith("http://ns.adobe.com/xap/1.0/", StringComparison.Ordinal))
                    {
                        string xmp = Encoding.UTF8.GetString(bytes, dataOffset + 29, dataLength - 29);
                        (string XmpTitle, string XmpAuthor, string XmpDescription) parsed = ReadXmp(xmp);
                        title = title.Length == 0 ? parsed.XmpTitle : title;
                        author = author.Length == 0 ? parsed.XmpAuthor : author;
                        if (parsed.XmpDescription.Length > 0)
                        {
                            descriptions.Add(parsed.XmpDescription);
                        }
                    }
                }
            }

            offset += 2 + segmentLength;
        }

        return new ImageMetadata(title, author, string.Join('\n', descriptions));
    }

    private readonly record struct ExifStrings(string Title, string Artist, string Description);

    /// <summary>
    /// Reads the handful of IFD0 tags that hold human-written text. Only IFD0 is walked: the tags
    /// worth having — the description, the artist, and Windows' own XP* strings — are all there,
    /// and following the EXIF sub-IFD pointer for a user comment would double the code for a field
    /// almost nothing writes.
    /// </summary>
    private static ExifStrings ReadExif(byte[] bytes, int tiffOffset, int length)
    {
        if (length < 8 || tiffOffset + 8 > bytes.Length)
        {
            return new ExifStrings(string.Empty, string.Empty, string.Empty);
        }

        bool littleEndian = bytes[tiffOffset] == 'I' && bytes[tiffOffset + 1] == 'I';
        int ifdOffset = ReadInt32(bytes, tiffOffset + 4, littleEndian);
        int directory = tiffOffset + ifdOffset;

        if (ifdOffset < 8 || directory + 2 > bytes.Length)
        {
            return new ExifStrings(string.Empty, string.Empty, string.Empty);
        }

        int entryCount = ReadInt16(bytes, directory, littleEndian);
        string title = string.Empty;
        string artist = string.Empty;
        string description = string.Empty;

        for (int entry = 0; entry < entryCount; entry++)
        {
            int entryOffset = directory + 2 + (entry * 12);
            if (entryOffset + 12 > bytes.Length)
            {
                break;
            }

            int tag = ReadInt16(bytes, entryOffset, littleEndian);
            int type = ReadInt16(bytes, entryOffset + 2, littleEndian);
            int count = ReadInt32(bytes, entryOffset + 4, littleEndian);

            if (tag is not (0x010E or 0x013B or 0x9C9B or 0x9C9C or 0x9C9E))
            {
                continue;
            }

            string? value = ReadExifValue(bytes, tiffOffset, entryOffset + 8, type, count, littleEndian);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            value = value.Trim();

            switch (tag)
            {
                case 0x010E: description = description.Length == 0 ? value : description; break;
                case 0x013B: artist = artist.Length == 0 ? value : artist; break;
                case 0x9C9B: title = title.Length == 0 ? value : title; break;
                case 0x9C9C when description.Length == 0: description = value; break;
                case 0x9C9E when description.Length == 0: description = value; break;
            }
        }

        return new ExifStrings(title, artist, description);
    }

    /// <summary>
    /// A tag's value. Values of four bytes or fewer are stored in the entry itself; anything longer
    /// is an offset from the start of the TIFF header — which is why <paramref name="tiffOffset"/>
    /// has to be carried all the way down here.
    /// </summary>
    private static string? ReadExifValue(
        byte[] bytes,
        int tiffOffset,
        int valueOffset,
        int type,
        int count,
        bool littleEndian)
    {
        // Type 2 is ASCII, one byte per element; type 1 is BYTE, which is how the XP* tags smuggle
        // in UTF-16 and why their byte count is twice their character count.
        int elementSize = type == 2 || type == 1 || type == 7 ? 1 : 0;
        if (elementSize == 0 || count <= 0 || count > 8192)
        {
            return null;
        }

        int dataOffset = count <= 4 ? valueOffset : tiffOffset + ReadInt32(bytes, valueOffset, littleEndian);
        if (dataOffset < 0 || dataOffset + count > bytes.Length)
        {
            return null;
        }

        if (type == 2)
        {
            int end = Array.IndexOf(bytes, (byte)0, dataOffset, count);
            int stringLength = end < 0 ? count : end - dataOffset;
            return Encoding.UTF8.GetString(bytes, dataOffset, stringLength);
        }

        // The XP* tags are null-terminated UTF-16LE regardless of the file's own byte order.
        string decoded = Encoding.Unicode.GetString(bytes, dataOffset, count - (count % 2));
        int terminator = decoded.IndexOf('\0');
        return terminator >= 0 ? decoded[..terminator] : decoded;
    }

    #endregion

    /// <summary>
    /// The Dublin Core fields out of an XMP packet. Matched textually rather than with an XML
    /// parser: an XMP packet extracted from a marker segment is routinely padded with whitespace and
    /// truncated by the header cap above, and a parser would reject the whole thing over either.
    /// </summary>
    private static (string Title, string Author, string Description) ReadXmp(string xmp) =>
    (
        FindXmlValue(xmp, "dc:title"),
        FindXmlValue(xmp, "dc:creator"),
        FindXmlValue(xmp, "dc:description")
    );

    private static string FindXmlValue(string xml, string elementName)
    {
        int open = xml.IndexOf('<' + elementName, StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            return string.Empty;
        }

        int close = xml.IndexOf("</" + elementName, open, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            return string.Empty;
        }

        // Dublin Core wraps its values in rdf:Alt/rdf:li scaffolding; stripping tags gets past it
        // without having to model any of it.
        string inner = xml[open..close];
        return MarkupTextExtractor.StripTags(inner).Replace('\n', ' ').Trim();
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static int ReadInt32(byte[] bytes, int offset, bool littleEndian)
    {
        if (offset + 4 > bytes.Length)
        {
            return 0;
        }

        return littleEndian
            ? bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24)
            : ReadBigEndianInt32(bytes, offset);
    }

    private static int ReadInt16(byte[] bytes, int offset, bool littleEndian)
    {
        if (offset + 2 > bytes.Length)
        {
            return 0;
        }

        return littleEndian
            ? bytes[offset] | (bytes[offset + 1] << 8)
            : (bytes[offset] << 8) | bytes[offset + 1];
    }
}
