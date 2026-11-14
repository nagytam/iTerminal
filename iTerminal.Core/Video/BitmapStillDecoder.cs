using SkiaSharp;

namespace iTerminal.Core.Video;

/// <summary>
/// A decoder for a picture that is decoded in-process into one bitmap and drawn, fitted, as a single frame. The bitmap is
/// made on first use and kept, so a reopen at another size does not decode the file again. Like the SVG decoder, every
/// frame after the first reports the end of the stream.
/// </summary>
public abstract class BitmapStillDecoder : IVideoDecoder
{
    private readonly Lazy<SKBitmap> _bitmap;
    private int _columns = 1;
    private int _rows = 1;
    private bool _delivered;

    protected BitmapStillDecoder(VideoInfo info, Func<SKBitmap> load)
    {
        Info = info;
        _bitmap = new Lazy<SKBitmap>(load);
    }

    public VideoInfo Info { get; }

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
        SKBitmap bitmap = _bitmap.Value;
        SKRect bounds = SKRect.Create(bitmap.Width, bitmap.Height);
        StillFrame.Fit(rgb, _columns, _rows, bounds, canvas =>
            canvas.DrawBitmap(bitmap, bounds, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)));
        return true;
    }

    public void Dispose()
    {
        if (_bitmap.IsValueCreated)
        {
            _bitmap.Value.Dispose();
        }
    }
}
