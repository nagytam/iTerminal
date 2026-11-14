using System.Globalization;

namespace iTerminal.Core.ScreenSavers;

/// <summary>Reads the screen saver's two settings, falling back to the defaults for a missing or unreadable value.</summary>
public static class ScreenSaverSettings
{
    /// <summary>The effect used when none is stored, or the stored name is unknown.</summary>
    public const ScreenSaverKind DefaultKind = ScreenSaverKind.Starfield;

    public const int DefaultIdleMinutes = 5;
    public const int MinIdleMinutes = 1;
    public const int MaxIdleMinutes = 240;

    public static ScreenSaverKind ParseKind(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out ScreenSaverKind kind) && Enum.IsDefined(kind)
            ? kind
            : DefaultKind;

    public static int ParseIdleMinutes(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes)
            ? Math.Clamp(minutes, MinIdleMinutes, MaxIdleMinutes)
            : DefaultIdleMinutes;
}
