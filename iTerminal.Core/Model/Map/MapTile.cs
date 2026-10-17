using SkiaSharp;

namespace iTerminal.Core.Model.Map;

/// <summary>Identifies one 256 × 256 tile: zoom, column (west to east) and row (north to south).</summary>
public readonly record struct MapTileKey(int Zoom, int X, int Y)
{
    /// <summary>The tile one zoom level out that contains this one.</summary>
    public MapTileKey Parent => new(Zoom - 1, X >> 1, Y >> 1);

    public override string ToString() => $"{Zoom}/{X}/{Y}";
}

/// <summary>A decoded tile: <see cref="Width"/> × <see cref="Height"/> pixels, four bytes (R, G, B, A) each.</summary>
public sealed class MapTile(int width, int height, byte[] rgba)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Rgba { get; } = rgba;

    /// <summary>Decodes a PNG or JPEG tile; null when the bytes are not an image.</summary>
    public static MapTile? Decode(byte[] bytes)
    {
        try
        {
            using var decoded = SKBitmap.Decode(bytes);
            if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
            {
                return null;
            }

            using var converted = decoded.ColorType == SKColorType.Rgba8888 && decoded.AlphaType == SKAlphaType.Unpremul
                ? decoded.Copy()
                : decoded.Copy(SKColorType.Rgba8888);
            return converted is null ? null : new MapTile(converted.Width, converted.Height, converted.GetPixelSpan().ToArray());
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
