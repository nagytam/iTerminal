namespace iTerminal.Core.Video;

/// <summary>
/// Reads the flattened composite of an Adobe Photoshop file (.psd, or .psb for the large format): the picture Photoshop
/// stores after the layers, so a layered file shows as its visible result. Only the layouts a picture uses are read:
/// 8 or 16 bits per channel, RGB, grey, CMYK and indexed colour, raw or run-length data. Anything else is refused with a
/// message rather than a blank picture.
/// </summary>
public sealed class PsdVideoDecoder : BitmapStillDecoder
{
    public PsdVideoDecoder(string path)
        : this(path, Header(path))
    {
    }

    private PsdVideoDecoder(string path, PsdHeader header)
        : base(new VideoInfo(header.Width, header.Height, TimeSpan.Zero, 0), () => PsdComposite.DecodeFile(path, header))
    {
    }

    /// <summary>True for the files this decoder reads: a Photoshop document, by its extension.</summary>
    public static bool IsPsdFile(string path) => ImageFileTypes.IsPhotoshopFile(path);

    private static PsdHeader Header(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return PsdComposite.ReadHeader(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            throw new InvalidOperationException(Unsupported(), exception);
        }
    }

    internal static string Unsupported() => iConsole.Localization.UiText.Get("Picture.PsdUnsupported", "This Photoshop file uses a layout the viewer does not read.");
}
