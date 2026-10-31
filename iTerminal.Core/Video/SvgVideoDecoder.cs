using SkiaSharp;
using Svg.Skia;

namespace iTerminal.Core.Video;

/// <summary>
/// Rasterizes an SVG drawing into one frame for the picture viewers, the way ffmpeg hands them a picture: the drawing is
/// fitted into the requested pixel size with its proportions kept, and the rest of the frame is black. A drawing is a
/// single still, so the first <see cref="ReadFrame"/> carries it and every later call reports the end of the stream.
/// </summary>
public sealed class SvgVideoDecoder : IVideoDecoder
{
    // The SVG document owns the picture it loads, so it lives as long as the decoder does.
    private readonly SKSvg _svg;
    private readonly SKPicture _picture;
    private readonly SKRect _bounds;
    private int _columns = 1;
    private int _rows = 1;
    private bool _delivered;

    public SvgVideoDecoder(string path)
    {
        _svg = new SKSvg();
        _picture = Load(_svg, path);
        _bounds = _picture.CullRect;
        int width = Math.Max(1, (int)Math.Ceiling(_bounds.Width));
        int height = Math.Max(1, (int)Math.Ceiling(_bounds.Height));
        Info = new VideoInfo(width, height, TimeSpan.Zero, 0);
    }

    /// <summary>True for the files this decoder reads: an SVG drawing, by its extension.</summary>
    public static bool IsSvgFile(string path) =>
        !string.IsNullOrEmpty(path) && string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase);

    public VideoInfo Info { get; }

    private static SKPicture Load(SKSvg svg, string path)
    {
        try
        {
            SKPicture? picture = svg.Load(path);
            if (picture is not null)
            {
                return picture;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidOperationException(Unreadable(), exception);
        }

        throw new InvalidOperationException(Unreadable());
    }

    private static string Unreadable() => iConsole.Localization.UiText.Get("Svg.NotReadable", "This SVG drawing could not be read.");

    public void Open(TimeSpan offset, int columns, int rows)
    {
        _columns = Math.Max(1, columns);
        _rows = Math.Max(1, rows);
        _delivered = false;
    }

    public bool ReadFrame(byte[] rgb)
    {
        if (_delivered)
        {
            return false;
        }

        _delivered = true;
        StillFrame.Fit(rgb, _columns, _rows, _bounds, canvas => canvas.DrawPicture(_picture));
        return true;
    }

    public void Dispose()
    {
        _svg.Dispose();
    }
}
