using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace iTerminal.Core.Services.Images;

/// <summary>One fact about an image: the group it belongs to ("Camera", "Capture", …), its name, and its value.</summary>
public sealed record ImageInfoField(string Group, string Name, string Value);

/// <summary>
/// The metadata an image carries about itself, read from the bytes: the format and dimensions, the PNG and JPEG text,
/// and the EXIF fields a camera writes (make, model, exposure, date, lens, GPS position). Metadata sits at the front of
/// these formats, so only a prefix of the file is read and no pixel is decoded.
/// </summary>
public static class ImageInfoReader
{
    /// <summary>How much of a file is read. The EXIF block of a camera file is at its front, well inside this.</summary>
    public const int PrefixBytes = 1 << 20;

    /// <summary>The eight bytes every PNG starts with. Bytes, not a text literal: 0x89 is not a character in UTF-8.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const ushort TagImageDescription = 0x010E;
    private const ushort TagMake = 0x010F;
    private const ushort TagModel = 0x0110;
    private const ushort TagOrientation = 0x0112;
    private const ushort TagSoftware = 0x0131;
    private const ushort TagDateTime = 0x0132;
    private const ushort TagArtist = 0x013B;
    private const ushort TagCopyright = 0x8298;
    private const ushort TagExifIfd = 0x8769;
    private const ushort TagGpsIfd = 0x8825;
    private const ushort TagExposureTime = 0x829A;
    private const ushort TagFNumber = 0x829D;
    private const ushort TagIsoSpeed = 0x8827;
    private const ushort TagDateTimeOriginal = 0x9003;
    private const ushort TagDateTimeDigitized = 0x9004;
    private const ushort TagFocalLength = 0x920A;
    private const ushort TagPixelXDimension = 0xA002;
    private const ushort TagPixelYDimension = 0xA003;
    private const ushort TagLensModel = 0xA434;
    private const ushort TagGpsLatitudeRef = 0x0001;
    private const ushort TagGpsLatitude = 0x0002;
    private const ushort TagGpsLongitudeRef = 0x0003;
    private const ushort TagGpsLongitude = 0x0004;
    private const ushort TagGpsAltitudeRef = 0x0005;
    private const ushort TagGpsAltitude = 0x0006;

    /// <summary>The metadata of an image file on disk. An unreadable file gives one field saying so.</summary>
    public static IReadOnlyList<ImageInfoField> ReadFile(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            byte[] prefix = new byte[Math.Min(PrefixBytes, stream.Length)];
            stream.ReadExactly(prefix);
            return Read(prefix, stream.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [new ImageInfoField("File", "Error", exception.Message)];
        }
    }

    /// <summary>The metadata held in a prefix of an image of the given total length.</summary>
    public static IReadOnlyList<ImageInfoField> Read(ReadOnlySpan<byte> bytes, long fileLength)
    {
        List<ImageInfoField> fields = [];
        try
        {
            if (bytes.StartsWith(PngSignature))
            {
                ReadPng(bytes, fields);
            }
            else if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                ReadJpeg(bytes, fields);
            }
            else if (bytes.StartsWith("GIF8"u8) && bytes.Length >= 10)
            {
                fields.Add(new("Image", "Format", "GIF"));
                fields.Add(new("Image", "Dimensions", $"{BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..])} × {BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..])} pixels"));
            }
            else if (bytes.StartsWith("BM"u8) && bytes.Length >= 26)
            {
                fields.Add(new("Image", "Format", "BMP"));
                int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[18..]);
                int height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(bytes[22..]));
                fields.Add(new("Image", "Dimensions", $"{width} × {height} pixels"));
            }
            else if (bytes.StartsWith("RIFF"u8) && bytes.Length >= 12 && bytes[8..12].SequenceEqual("WEBP"u8))
            {
                fields.Add(new("Image", "Format", "WebP"));
            }
            else if ((bytes.StartsWith("II*\0"u8) || bytes.StartsWith("MM\0*"u8)))
            {
                fields.Add(new("Image", "Format", "TIFF"));
                AddExif(bytes, fields);
            }
            else
            {
                fields.Add(new("Image", "Format", "Unknown"));
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // A truncated or damaged header: what was read before the damage is what there is.
        }

        fields.Add(new("File", "Size", $"{fileLength:N0} bytes"));
        return fields;
    }

    private static void ReadPng(ReadOnlySpan<byte> bytes, List<ImageInfoField> fields)
    {
        fields.Add(new("Image", "Format", "PNG"));
        int position = 8;
        while (position + 12 <= bytes.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes[position..]);
            string type = Encoding.ASCII.GetString(bytes.Slice(position + 4, 4));
            int data = position + 8;
            if (data + length > bytes.Length)
            {
                break;
            }

            ReadOnlySpan<byte> chunk = bytes.Slice(data, (int)length);
            if (type == "IHDR" && chunk.Length >= 8)
            {
                fields.Add(new("Image", "Dimensions", $"{BinaryPrimitives.ReadUInt32BigEndian(chunk)} × {BinaryPrimitives.ReadUInt32BigEndian(chunk[4..])} pixels"));
            }
            else if (type == "tEXt")
            {
                int separator = chunk.IndexOf((byte)0);
                if (separator > 0)
                {
                    fields.Add(new("Text", Encoding.Latin1.GetString(chunk[..separator]), Encoding.Latin1.GetString(chunk[(separator + 1)..])));
                }
            }
            else if (type == "IEND")
            {
                break;
            }

            position = data + (int)length + 4;
        }
    }

    private static void ReadJpeg(ReadOnlySpan<byte> bytes, List<ImageInfoField> fields)
    {
        fields.Add(new("Image", "Format", "JPEG"));
        int position = 2;
        while (position + 4 <= bytes.Length && bytes[position] == 0xFF)
        {
            byte marker = bytes[position + 1];
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0xFF)
            {
                position += marker == 0xFF ? 1 : 2;
                continue;
            }

            if (marker == 0xDA)
            {
                // Start of the image data: the metadata is all before it.
                break;
            }

            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[(position + 2)..]);
            int data = position + 4;
            if (data + length - 2 > bytes.Length)
            {
                break;
            }

            ReadOnlySpan<byte> segment = bytes.Slice(data, length - 2);
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC) && segment.Length >= 5)
            {
                fields.Add(new("Image", "Dimensions", $"{BinaryPrimitives.ReadUInt16BigEndian(segment[3..])} × {BinaryPrimitives.ReadUInt16BigEndian(segment[1..])} pixels"));
            }
            else if (marker == 0xE1 && segment.StartsWith("Exif\0\0"u8))
            {
                AddExif(segment[6..], fields);
            }
            else if (marker == 0xFE)
            {
                fields.Add(new("Text", "Comment", Encoding.Latin1.GetString(segment).TrimEnd('\0')));
            }

            position = data + length - 2;
        }
    }

    /// <summary>Reads the TIFF structure of an EXIF block: IFD0, and the EXIF and GPS pointers in it.</summary>
    private static void AddExif(ReadOnlySpan<byte> tiff, List<ImageInfoField> fields)
    {
        if (tiff.Length < 8)
        {
            return;
        }

        bool bigEndian = tiff[0] == 'M';
        var entries = new Dictionary<ushort, Entry>();
        ReadIfd(tiff, bigEndian, (int)ReadUInt32(tiff[4..], bigEndian), entries);
        ushort exifOffset = 0;
        ushort gpsOffset = 0;
        if (entries.TryGetValue(TagExifIfd, out Entry exifPointer))
        {
            exifOffset = (ushort)exifPointer.Number(0);
            ReadIfd(tiff, bigEndian, exifOffset, entries);
        }

        if (entries.TryGetValue(TagGpsIfd, out Entry gpsPointer))
        {
            gpsOffset = (ushort)gpsPointer.Number(0);
            ReadIfd(tiff, bigEndian, gpsOffset, entries, gps: true);
        }

        AddText(entries, TagMake, "Camera", "Make", fields);
        AddText(entries, TagModel, "Camera", "Model", fields);
        AddText(entries, TagLensModel, "Camera", "Lens", fields);
        AddText(entries, TagDateTimeOriginal, "Capture", "Taken", fields, FormatDate);
        AddText(entries, TagDateTimeDigitized, "Capture", "Digitised", fields, FormatDate);
        AddText(entries, TagDateTime, "Capture", "Modified", fields, FormatDate);
        AddRational(entries, TagExposureTime, "Capture", "Exposure", fields, FormatExposure);
        AddRational(entries, TagFNumber, "Capture", "Aperture", fields, v => $"f/{v.ToString("0.#", CultureInfo.InvariantCulture)}");
        AddRational(entries, TagFocalLength, "Capture", "Focal length", fields, v => $"{v.ToString("0.#", CultureInfo.InvariantCulture)} mm");
        AddNumber(entries, TagIsoSpeed, "Capture", "ISO", fields);
        AddText(entries, TagArtist, "Image", "Artist", fields);
        AddText(entries, TagCopyright, "Image", "Copyright", fields);
        AddText(entries, TagSoftware, "Image", "Software", fields);
        AddText(entries, TagImageDescription, "Image", "Description", fields);
        AddNumber(entries, TagOrientation, "Image", "Orientation", fields);
        if (entries.TryGetValue(TagPixelXDimension, out Entry px) && entries.TryGetValue(TagPixelYDimension, out Entry py))
        {
            fields.Add(new("Image", "Dimensions (EXIF)", $"{px.Number(0)} × {py.Number(0)} pixels"));
        }

        AddGps(entries, fields);
    }

    /// <summary>One IFD entry, with the bytes of its value (inline or at its offset).</summary>
    private readonly record struct Entry(ushort Tag, ushort Type, uint Count, ReadOnlyMemory<byte> Value, bool BigEndian)
    {
        public double Number(int index)
        {
            ReadOnlySpan<byte> v = Value.Span;
            return Type switch
            {
                3 => ReadUInt16(v[(index * 2)..], BigEndian),
                4 or 9 => ReadUInt32(v[(index * 4)..], BigEndian),
                _ => 0,
            };
        }

        public (double Numerator, double Denominator) Rational(int index)
        {
            ReadOnlySpan<byte> v = Value.Span[(index * 8)..];
            return (ReadUInt32(v, BigEndian), ReadUInt32(v[4..], BigEndian));
        }

        public string Text() => Encoding.UTF8.GetString(Value.Span).TrimEnd('\0', ' ');
    }

    private static void ReadIfd(ReadOnlySpan<byte> tiff, bool bigEndian, int offset, Dictionary<ushort, Entry> entries, bool gps = false)
    {
        if (offset <= 0 || offset + 2 > tiff.Length)
        {
            return;
        }

        int count = ReadUInt16(tiff[offset..], bigEndian);
        for (int i = 0; i < count; i++)
        {
            int at = offset + 2 + i * 12;
            if (at + 12 > tiff.Length)
            {
                return;
            }

            ushort tag = ReadUInt16(tiff[at..], bigEndian);
            ushort type = ReadUInt16(tiff[(at + 2)..], bigEndian);
            uint count2 = ReadUInt32(tiff[(at + 4)..], bigEndian);
            int size = TypeSize(type) * (int)count2;
            if (size <= 0 || size > tiff.Length)
            {
                continue;
            }

            ReadOnlyMemory<byte> value;
            if (size <= 4)
            {
                value = tiff.Slice(at + 8, size).ToArray();
            }
            else
            {
                int valueOffset = (int)ReadUInt32(tiff[(at + 8)..], bigEndian);
                if (valueOffset < 0 || valueOffset + size > tiff.Length)
                {
                    continue;
                }

                value = tiff.Slice(valueOffset, size).ToArray();
            }

            // GPS tags are numbered on their own, so they are kept apart from the main IFD's.
            ushort key = gps ? (ushort)(0x8000 | tag) : tag;
            entries[key] = new Entry(tag, type, count2, value, bigEndian);
        }
    }

    private static int TypeSize(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8 => 2,
        4 or 9 or 11 => 4,
        5 or 10 or 12 => 8,
        _ => 0,
    };

    private static void AddText(Dictionary<ushort, Entry> entries, ushort tag, string group, string name, List<ImageInfoField> fields, Func<string, string>? format = null)
    {
        if (entries.TryGetValue(tag, out Entry entry) && entry.Type == 2 && entry.Text().Length > 0)
        {
            string text = entry.Text();
            fields.Add(new(group, name, format is null ? text : format(text)));
        }
    }

    private static void AddNumber(Dictionary<ushort, Entry> entries, ushort tag, string group, string name, List<ImageInfoField> fields)
    {
        if (entries.TryGetValue(tag, out Entry entry) && entry.Type is 3 or 4 or 9)
        {
            fields.Add(new(group, name, entry.Number(0).ToString(CultureInfo.InvariantCulture)));
        }
    }

    private static void AddRational(Dictionary<ushort, Entry> entries, ushort tag, string group, string name, List<ImageInfoField> fields, Func<double, string> format)
    {
        if (entries.TryGetValue(tag, out Entry entry) && entry.Type is 5 or 10 && entry.Count > 0)
        {
            var (numerator, denominator) = entry.Rational(0);
            if (denominator != 0)
            {
                fields.Add(new(group, name, format(numerator / denominator)));
            }
        }
    }

    private static void AddGps(Dictionary<ushort, Entry> entries, List<ImageInfoField> fields)
    {
        if (!entries.TryGetValue(0x8000 | TagGpsLatitude, out Entry latitude) || !entries.TryGetValue(0x8000 | TagGpsLongitude, out Entry longitude))
        {
            return;
        }

        string latitudeRef = entries.TryGetValue(0x8000 | TagGpsLatitudeRef, out Entry latRef) ? latRef.Text() : "N";
        string longitudeRef = entries.TryGetValue(0x8000 | TagGpsLongitudeRef, out Entry lonRef) ? lonRef.Text() : "E";
        double lat = Degrees(latitude) * (latitudeRef.StartsWith('S') ? -1 : 1);
        double lon = Degrees(longitude) * (longitudeRef.StartsWith('W') ? -1 : 1);
        fields.Add(new("Location", "Position", $"{lat.ToString("0.000000", CultureInfo.InvariantCulture)}, {lon.ToString("0.000000", CultureInfo.InvariantCulture)}"));
        if (entries.TryGetValue(0x8000 | TagGpsAltitude, out Entry altitude) && altitude.Count > 0)
        {
            var (numerator, denominator) = altitude.Rational(0);
            if (denominator != 0)
            {
                fields.Add(new("Location", "Altitude", $"{(numerator / denominator).ToString("0.#", CultureInfo.InvariantCulture)} m"));
            }
        }
    }

    /// <summary>Degrees, minutes and seconds, each a rational, to decimal degrees.</summary>
    private static double Degrees(Entry entry)
    {
        double total = 0;
        double scale = 1;
        for (int i = 0; i < 3 && i < entry.Count; i++)
        {
            var (numerator, denominator) = entry.Rational(i);
            total += denominator == 0 ? 0 : numerator / denominator / scale;
            scale *= 60;
        }

        return total;
    }

    private static string FormatExposure(double seconds) =>
        seconds >= 1 || seconds <= 0
            ? $"{seconds.ToString("0.#", CultureInfo.InvariantCulture)} s"
            : $"1/{Math.Round(1 / seconds).ToString(CultureInfo.InvariantCulture)} s";

    /// <summary>"2024:05:01 10:20:30" as "2024-05-01 10:20:30"; anything else as it was written.</summary>
    private static string FormatDate(string text) =>
        text.Length == 19 && text[4] == ':' && text[7] == ':'
            ? $"{text[..4]}-{text[5..7]}-{text[8..10]} {text[11..]}"
            : text;

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool bigEndian) =>
        bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
}
