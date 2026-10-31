namespace iTerminal.Core.Data;

public static class Settings
{
    public const string IsQuotesIndicatorEnabled = nameof(IsQuotesIndicatorEnabled);
    public const string IsWeatherIndicatorEnabled = nameof(IsWeatherIndicatorEnabled);
    public const string NetworkConnectionProfiles = nameof(NetworkConnectionProfiles);

    /// <summary>The saved SSH and PowerShell remoting connections of the remote connection manager, as JSON.</summary>
    public const string RemoteConnections = nameof(RemoteConnections);

    /// <summary>The saved workspaces (named screen layouts) of the workspace manager, as JSON, in the order they were saved.</summary>
    public const string Workspaces = nameof(Workspaces);

    /// <summary>The name of the workspace last saved or switched to.</summary>
    public const string ActiveWorkspace = nameof(ActiveWorkspace);

    /// <summary>The language of the interface chosen in the language dialog (<c>en</c>, <c>de</c>, ...), restored at start-up.</summary>
    public const string Language = nameof(Language);

    /// <summary>The colour theme chosen in the theme manager, restored at start-up.</summary>
    public const string Theme = nameof(Theme);

    /// <summary>Root folder the music library indexer walks. Empty means the OS "Music" folder.</summary>
    public const string MusicFolder = nameof(MusicFolder);

    public const string MusicVolume = nameof(MusicVolume);
    public const string MusicOutputDeviceId = nameof(MusicOutputDeviceId);
    public const string MusicRepeatMode = nameof(MusicRepeatMode);
    public const string MusicShuffle = nameof(MusicShuffle);

    /// <summary>
    /// Position in the queue of the track that was cued when the app closed. A setting rather than a
    /// column on the queue table because it is a cursor into that list, not a property of any row,
    /// and it moves — every time a track ends — without the queue itself changing at all.
    /// </summary>
    public const string MusicCurrentIndex = nameof(MusicCurrentIndex);

    /// <summary>
    /// Root folder the document indexer walks. Empty means the built-in default, which
    /// <see cref="Services.Documents.DocumentLibraryService.ResolveDocumentsFolder"/> resolves.
    /// </summary>
    public const string DocumentsFolder = nameof(DocumentsFolder);

    /// <summary>
    /// Whether images are put through OCR while indexing. Off by default: OCR is seconds per image
    /// and only pays for itself on a folder of scans, so it is the user's call rather than
    /// something a first run springs on them.
    /// </summary>
    public const string DocumentsOcrEnabled = nameof(DocumentsOcrEnabled);

    /// <summary>
    /// The feature flag for Sixel bitmaps (the image viewer and the splash emblem). On by default, and then used only where the
    /// terminal is detected to draw Sixel; off draws the ASCII art and the text splash everywhere. Read live, so it applies the
    /// next time a picture is drawn.
    /// </summary>
    public const string SixelEnabled = nameof(SixelEnabled);
}
