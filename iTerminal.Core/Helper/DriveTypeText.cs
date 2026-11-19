using iConsole.Localization;

namespace iTerminal.Core.Helper;

/// <summary>
/// A drive's type as a word in the chosen language. The drive lists printed the enum's own name ("Fixed", "Removable", "CDRom") in the
/// middle of otherwise translated dialogs.
/// </summary>
internal static class DriveTypeText
{
    /// <summary>The word for <paramref name="type"/>; empty for none. In English it is the name the enum has always printed.</summary>
    public static string Of(DriveType? type) => type switch
    {
        null => string.Empty,
        DriveType.Fixed => UiText.Get("DriveType.Fixed", "Fixed"),
        DriveType.Removable => UiText.Get("DriveType.Removable", "Removable"),
        DriveType.CDRom => UiText.Get("DriveType.CDRom", "CDRom"),
        DriveType.Network => UiText.Get("DriveType.Network", "Network"),
        DriveType.Ram => UiText.Get("DriveType.Ram", "Ram"),
        DriveType.NoRootDirectory => UiText.Get("DriveType.NoRootDirectory", "NoRootDirectory"),
        _ => UiText.Get("DriveType.Unknown", "Unknown"),
    };
}
