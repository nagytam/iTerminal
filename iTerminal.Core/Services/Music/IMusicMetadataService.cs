using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// Everything the music file viewer needs to know about a file before it plays it. Reading tags and
/// probing a decoder both touch the disk, so the contract is async and the work happens off the UI
/// thread — the same rule the rest of the app's feature services follow.
/// </summary>
public interface IMusicMetadataService
{
    /// <summary>True when the extension names an audio format, whether or not it can be played here.</summary>
    bool IsMusicFile(string path);

    /// <summary>
    /// Tags plus playing time and audio format. Never throws for an unreadable file: the failure is
    /// reported in <see cref="MusicFileMetadata.Error"/> so the viewer can show it.
    /// </summary>
    Task<MusicFileMetadata> ReadAsync(string path, CancellationToken cancellationToken = default);
}
