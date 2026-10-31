using SkiaSharp;

namespace iTerminal.Core.Video;

/// <summary>
/// Decodes an icon file (.ico) into one frame for the picture viewers. An icon that holds several sizes shows the one
/// SkiaSharp picks, which is the largest; the picture is fitted into the requested pixel size with its proportions kept.
/// </summary>
public sealed class IconVideoDecoder : IVideoDecoder
{
    private readonly SKBitmap _bitmap;
    private int _columns = 1;
    private int _rows = 1;
    private bool _delivered;

    public IconVideoDecoder(string path)
    {
        _bitmap = SKBitmap.Decode(path) ?? throw new InvalidOperationException(Unreadable());
        Info = new VideoInfo(Math.Max(1, _bitmap.Width), Math.Max(1, _bitmap.Height), TimeSpan.Zero, 0);
    }

    /// <summary>True for the files this decoder reads: an icon, by its extension.</summary>
    public static bool IsIconFile(string path) =>
        !string.IsNullOrEmpty(path) && string.Equals(Path.GetExtension(path), ".ico", StringComparison.OrdinalIgnoreCase);

    public VideoInfo Info { get; }

    private static string Unreadable() => iConsole.Localization.UiText.Get("Icon.NotReadable", "This icon file could not be read.");

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
        var bounds = new SKRect(0, 0, Info.Width, Info.Height);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        StillFrame.Fit(rgb, _columns, _rows, bounds, canvas => canvas.DrawBitmap(_bitmap, bounds, sampling));
        return true;
    }

    public void Dispose() => _bitmap.Dispose();
}
