namespace iTerminal.Core.Services.BackgroundStart;

/// <summary>
/// The background services whose start is configurable, by the id their setting is stored under.
/// Weather and quotes are not among them: their figures are the menu bar's own indicators, shown all the time.
/// </summary>
public static class BackgroundServices
{
    public const string FolderIndex = nameof(FolderIndex);
    public const string DocumentLibrary = nameof(DocumentLibrary);
    public const string MusicLibrary = nameof(MusicLibrary);
    public const string OllamaServer = nameof(OllamaServer);
    public const string UpdateCheck = nameof(UpdateCheck);
    public const string RuntimeDependencyCheck = nameof(RuntimeDependencyCheck);
    public const string NetworkInfo = nameof(NetworkInfo);

    /// <summary>Every configurable service, in the order the settings page lists them, with the label it shows.</summary>
    public static IReadOnlyList<(string Id, string Label)> All { get; } =
    [
        (FolderIndex, "Folder index (folder selector)"),
        (DocumentLibrary, "Document library (document manager)"),
        (MusicLibrary, "Music library (music catalog)"),
        (OllamaServer, "Local AI server (AI bar)"),
        (UpdateCheck, "Update check (About)"),
        (RuntimeDependencyCheck, "Runtime dependency check (Runtime dependencies)"),
        (NetworkInfo, "Network interfaces (network manager)"),
    ];
}
