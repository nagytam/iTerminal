using SkiaSharp;

namespace iTerminal.Core.Video;

/// <summary>
/// Shows a camera raw file as the JPEG preview the camera embedded in it. The sensor data is never demosaiced: a raw
/// file that holds no decodable JPEG is refused with a message. Almost every camera stores one (the full-size preview
/// or, failing that, a thumbnail), and the largest one that decodes is the picture shown.
/// </summary>
public sealed class RawPreviewVideoDecoder : BitmapStillDecoder
{
    /// <summary>Raw files are read whole to find the preview, so a larger one is refused rather than loaded.</summary>
    internal const long MaxFileBytes = 1L << 30;

    /// <summary>How many of the largest candidates are tried before giving up: a false match in the sensor data is small.</summary>
    private const int CandidatesTried = 4;

    private static readonly byte[] JpegStart = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] JpegEnd = [0xFF, 0xD9];

    public RawPreviewVideoDecoder(string path)
        : this(Preview(path))
    {
    }

    private RawPreviewVideoDecoder(SKBitmap preview)
        : base(new VideoInfo(preview.Width, preview.Height, TimeSpan.Zero, 0), () => preview)
    {
    }

    /// <summary>True for the files this decoder reads: a camera raw, by its extension.</summary>
    public static bool IsRawFile(string path) => ImageFileTypes.IsRawFile(path);

    /// <summary>Finds the embedded JPEG previews of <paramref name="bytes"/>, largest first.</summary>
    internal static List<(int Start, int Length)> FindJpegs(ReadOnlySpan<byte> bytes)
    {
        var found = new List<(int Start, int Length)>();
        int at = 0;
        while (at < bytes.Length)
        {
            int start = bytes[at..].IndexOf(JpegStart);
            if (start < 0)
            {
                break;
            }

            start += at;
            int end = bytes[(start + JpegStart.Length)..].IndexOf(JpegEnd);
            if (end < 0)
            {
                break;
            }

            int length = start + JpegStart.Length + end + JpegEnd.Length - start;
            // A false match in the sensor data is rejected here, before a native decoder is asked to read it: its header is not a JPEG's.
            if (HasJpegHeader(bytes.Slice(start, length)))
            {
                found.Add((start, length));
            }
            at = start + 1;
        }

        return found.OrderByDescending(f => f.Length).ToList();
    }

    /// <summary>
    /// True when the bytes start with a well-formed JPEG header: the start-of-image marker, then marker segments, each with
    /// its length, up to the start of the scan. The scan itself is not checked.
    /// </summary>
    internal static bool HasJpegHeader(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return false;
        }

        int at = 2;
        while (at + 4 <= jpeg.Length)
        {
            if (jpeg[at] != 0xFF)
            {
                return false;
            }

            byte marker = jpeg[at + 1];
            if (marker == 0xDA)
            {
                return true;
            }

            // Only segments with a length may come before the scan: a fill byte, a stuffed 00, or a standalone marker (restart, SOI, EOI) does not.
            if (marker is < 0xC0 or 0xFF || marker is >= 0xD0 and <= 0xD9)
            {
                return false;
            }

            int length = (jpeg[at + 2] << 8) | jpeg[at + 3];
            if (length < 2)
            {
                return false;
            }

            at += 2 + length;
        }

        return false;
    }

    private static SKBitmap Preview(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
        {
            throw new InvalidOperationException(Unreadable());
        }

        byte[] bytes = File.ReadAllBytes(path);
        foreach ((int start, int length) in FindJpegs(bytes).Take(CandidatesTried))
        {
            // SKBitmap.Decode throws on a codec it cannot make, so the codec is checked first: a false match is skipped, not fatal.
            using SKData data = SKData.CreateCopy(bytes.AsSpan(start, length));
            using SKCodec? codec = SKCodec.Create(data);
            if (codec is null)
            {
                continue;
            }

            SKBitmap? bitmap = SKBitmap.Decode(codec);
            if (bitmap is not null)
            {
                return bitmap;
            }
        }

        throw new InvalidOperationException(NoPreview());
    }

    internal static string Unreadable() => iConsole.Localization.UiText.Get("Picture.RawUnreadable", "This raw file could not be read.");

    internal static string NoPreview() => iConsole.Localization.UiText.Get("Picture.RawNoPreview", "This raw file has no JPEG preview the viewer can show.");
}
