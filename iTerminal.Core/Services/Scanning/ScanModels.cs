namespace iTerminal.Core.Services.Scanning;

/// <summary>What the scanner is asked for: resolution in dots per inch and the colour mode.</summary>
public sealed record ScanOptions(int Dpi, ScanColorMode ColorMode, ScanSource Source)
{
    /// <summary>The resolutions the dialog cycles through.</summary>
    public static readonly int[] Resolutions = [150, 300, 600, 1200];

    public static ScanOptions Default { get; } = new(300, ScanColorMode.Color, ScanSource.Flatbed);
}

public enum ScanColorMode
{
    Color,
    Grayscale,
    BlackAndWhite,
}

/// <summary>
/// Where the page comes from. <see cref="Flatbed"/> scans one page from the glass. <see cref="Feeder"/> is the
/// automatic document feeder (ADF): every sheet in it is scanned, one page after another, until it is empty.
/// </summary>
public enum ScanSource
{
    Flatbed,
    Feeder,
}

/// <summary>The format a scan is saved in. Both are written through SkiaSharp.</summary>
public enum ScanFileFormat
{
    Png,
    Jpeg,
}

/// <summary>A scanner the platform reports. <see cref="Id"/> is what the backend needs to open it again.</summary>
public sealed record ScannerDevice(string Id, string Name);

/// <summary>The scanners, or why there are none. <see cref="IsSupported"/> is false where no backend exists at all.</summary>
public sealed record ScannerListing(bool IsSupported, IReadOnlyList<ScannerDevice> Scanners, string? Failure);

/// <summary>
/// One scanned page as the backend delivered it: a BMP on Windows (TWAIN), a PNG on Linux (SANE). Decoded
/// with SkiaSharp wherever it is previewed or saved, so the backends do not have to agree on a format.
/// </summary>
public sealed record ScanPage(byte[] Encoded);

/// <summary>The pages a scan produced, in the order the scanner delivered them, or a sentence saying why there are none.</summary>
public sealed record ScanResult(IReadOnlyList<ScanPage> Pages, string Message)
{
    public bool Success => Pages.Count > 0;

    public static ScanResult Failed(string message) => new([], message);
}

/// <summary>Whether a page was written to disk, and what to tell the user.</summary>
public sealed record SaveResult(bool Success, string Message);
