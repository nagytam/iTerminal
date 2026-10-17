namespace iTerminal.Core.Model.Map;

/// <summary>
/// The Web Mercator ("slippy map") maths OpenStreetMap tiles use: at zoom <c>z</c> the world is a square of
/// <c>256 · 2^z</c> pixels, cut into <c>2^z × 2^z</c> tiles of 256 × 256 pixels, with x growing east and y south.
/// </summary>
public static class MapProjection
{
    public const int TileSize = 256;
    public const int MinZoom = 0;
    public const int MaxZoom = 19;
    public const double MaxLatitude = 85.0511287798;

    public static double WorldSize(int zoom) => TileSize * Math.Pow(2, zoom);

    public static int TilesPerSide(int zoom) => 1 << zoom;

    public static double ClampLatitude(double latitude) => Math.Clamp(latitude, -MaxLatitude, MaxLatitude);

    /// <summary>Maps any longitude into [-180, 180).</summary>
    public static double WrapLongitude(double longitude)
    {
        var wrapped = (longitude + 180) % 360;
        if (wrapped < 0)
        {
            wrapped += 360;
        }

        return wrapped - 180;
    }

    public static (double X, double Y) ToPixel(double latitude, double longitude, int zoom)
    {
        var size = WorldSize(zoom);
        var sin = Math.Sin(ClampLatitude(latitude) * Math.PI / 180);
        var x = (longitude + 180) / 360 * size;
        var y = (0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * size;
        return (x, y);
    }

    public static (double Latitude, double Longitude) FromPixel(double x, double y, int zoom)
    {
        var size = WorldSize(zoom);
        var longitude = WrapLongitude(x / size * 360 - 180);
        var n = Math.PI - 2 * Math.PI * Math.Clamp(y, 0, size) / size;
        var latitude = 180 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
        return (ClampLatitude(latitude), longitude);
    }

    /// <summary>Ground distance one pixel covers at this latitude and zoom.</summary>
    public static double MetersPerPixel(double latitude, int zoom) =>
        156543.03392804097 * Math.Cos(ClampLatitude(latitude) * Math.PI / 180) / Math.Pow(2, zoom);

    /// <summary>"1.2 km" / "350 m" for a distance in metres.</summary>
    public static string FormatDistance(double meters) => meters >= 1000
        ? (meters >= 10000 ? $"{meters / 1000:0} km" : $"{meters / 1000:0.#} km")
        : $"{meters:0} m";

    /// <summary>The largest 1, 2 or 5 × 10ⁿ metre distance not longer than <paramref name="meters"/>.</summary>
    public static double NiceDistance(double meters)
    {
        if (meters <= 0)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(meters)));
        foreach (var step in new[] { 5.0, 2.0, 1.0 })
        {
            if (step * magnitude <= meters)
            {
                return step * magnitude;
            }
        }

        return magnitude;
    }

    public static string FormatCoordinates(double latitude, double longitude) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{Math.Abs(latitude):0.0000}°{(latitude >= 0 ? 'N' : 'S')} {Math.Abs(longitude):0.0000}°{(longitude >= 0 ? 'E' : 'W')}");

    /// <summary>
    /// Reads "47.4979, 19.0402", "47.4979 19.0402" or "47.4979N 19.0402E" (decimal degrees, comma or space apart).
    /// </summary>
    public static bool TryParseCoordinates(string? text, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Replace('°', ' ').Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !TryParseDegrees(parts[0], 'N', 'S', out latitude) || !TryParseDegrees(parts[1], 'E', 'W', out longitude))
        {
            return false;
        }

        if (Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
        {
            return false;
        }

        return true;
    }

    private static bool TryParseDegrees(string text, char positive, char negative, out double value)
    {
        var sign = 1;
        var last = char.ToUpperInvariant(text[^1]);
        if (last == positive || last == negative)
        {
            sign = last == negative ? -1 : 1;
            text = text[..^1];
        }

        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            value *= sign;
            return true;
        }

        return false;
    }
}
