using SkiaSharp;

namespace iTerminal.Core.Video;

/// <summary>
/// Draws a still picture into one RGB frame the way the picture viewers expect it from ffmpeg: the picture is fitted
/// into the frame with its proportions kept, centred, and the rest of the frame is black.
/// </summary>
internal static class StillFrame
{
    /// <param name="rgb">The frame to fill: <paramref name="columns"/> × <paramref name="rows"/> pixels, three bytes each.</param>
    /// <param name="bounds">The picture's own area, whose origin is moved to the frame's origin before drawing.</param>
    /// <param name="draw">Draws the picture into the canvas, in the picture's own coordinates.</param>
    public static void Fit(byte[] rgb, int columns, int rows, SKRect bounds, Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(columns, rows, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);

            float scale = Math.Min(columns / bounds.Width, rows / bounds.Height);
            float left = (columns - bounds.Width * scale) / 2f;
            float top = (rows - bounds.Height * scale) / 2f;
            canvas.Translate(left, top);
            canvas.Scale(scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            draw(canvas);
        }

        SKColor[] pixels = bitmap.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            rgb[i * 3] = pixels[i].Red;
            rgb[i * 3 + 1] = pixels[i].Green;
            rgb[i * 3 + 2] = pixels[i].Blue;
        }
    }
}
