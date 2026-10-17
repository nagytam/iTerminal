using iTerminal.Core.Services;
using iTerminal.Core.Services.Web;
using SkiaSharp;

namespace iTerminal.Core.Test.Model.Map;

/// <summary>Builds 256 × 256 PNG tiles and answers tile requests from memory.</summary>
internal static class MapTestSupport
{
    public static readonly SKColor Land = new(242, 239, 233);
    public static readonly SKColor Water = new(170, 211, 223);
    public static readonly SKColor Park = new(200, 250, 204);

    public static byte[] Png(SKColor background, Action<SKCanvas>? draw = null)
    {
        using var bitmap = new SKBitmap(256, 256);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(background);
            draw?.Invoke(canvas);
        }

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    /// <summary>
    /// Deletes a test folder. A background tile load that outlives its test can still be writing into it, so
    /// a failed delete is tried again a few times before giving up quietly.
    /// </summary>
    public static void DeleteFolder(string folder)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(folder); attempt++)
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>A land tile with a dark road across its middle.</summary>
    public static byte[] RoadPng() => Png(Land, canvas =>
    {
        using var paint = new SKPaint { Color = SKColors.Black, StrokeWidth = 6 };
        canvas.DrawLine(0, 128, 256, 128, paint);
    });
}

internal sealed class MemorySettings : ISettingsService
{
    private readonly Dictionary<string, string> _values = [];
    public bool IsDirty => false;
    public string? Get(string key) => _values.GetValueOrDefault(key);
    public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;
    public void Set(string key, string value) => _values[key] = value;
    public void Write()
    {
    }
}

internal sealed class FakeTileFetcher(Func<string, byte[]?> answer) : IWebFetcher
{
    private int _requests;

    public int Requests => Volatile.Read(ref _requests);

    public List<string> Urls { get; } = [];

    public Task<WebFetchResponse> FetchAsync(WebFetchRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _requests);
        lock (Urls)
        {
            Urls.Add(request.Url);
        }

        var bytes = answer(request.Url);
        if (bytes is null)
        {
            throw new WebFetchException("No such host: " + request.Url);
        }

        return Task.FromResult(new WebFetchResponse(request.Url, 200, "OK", "image/png", bytes));
    }
}
