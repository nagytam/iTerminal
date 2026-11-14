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
            found.Add((start, length));
            at = start + 1;
        }

        return found.OrderByDescending(f => f.Length).ToList();
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
