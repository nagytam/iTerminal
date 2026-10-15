using iTerminal.Core.Data;
using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// Reads the saved session out of the database and the settings and installs it in the player. See
/// <see cref="IMusicSessionRestoreService"/> for why this is not simply a step in the session job.
/// </summary>
/// <param name="playlistService">
/// Where the pre-database queue file is read from, or null for "there is no legacy queue to import".
/// It is optional because it is the one dependency that reaches outside the database — into the
/// user's playlists folder — and a fallback instance built without a configured app has no business
/// going there.
/// </param>
public sealed class MusicSessionRestoreService(
    IMusicPlayerService musicPlayerService,
    IMusicPlaylistStore musicPlaylistStore,
    IPlaylistService? playlistService,
    ISettingsService settingsService) : IMusicSessionRestoreService
{
    private readonly Lock _lock = new();

    private bool _isRestored;

    public bool RestoreOnce()
    {
        lock (_lock)
        {
            if (_isRestored)
            {
                return false;
            }

            _isRestored = true;
        }

        try
        {
            RestoreSettings();
            RestoreQueue();
        }
        catch
        {
            // A corrupt saved session must not stop the app from starting, and must not leave the
            // flag unset either: retrying a read that already failed once, on every panel that opens,
            // would only repeat the failure.
        }

        return true;
    }

    private void RestoreSettings()
    {
        if (int.TryParse(settingsService.Get(Settings.MusicVolume), out int volume))
        {
            musicPlayerService.Volume = volume;
        }

        string? deviceId = settingsService.Get(Settings.MusicOutputDeviceId);
        if (!string.IsNullOrEmpty(deviceId))
        {
            musicPlayerService.SetDevice(deviceId);
        }

        if (Enum.TryParse(settingsService.Get(Settings.MusicRepeatMode), out RepeatMode repeatMode))
        {
            musicPlayerService.RepeatMode = repeatMode;
        }

        musicPlayerService.IsShuffled = settingsService.GetAsBool(Settings.MusicShuffle) ?? false;
    }

    private void RestoreQueue()
    {
        IReadOnlyList<MusicTrack> tracks = musicPlaylistStore.Load();
        if (tracks.Count == 0 && playlistService is not null)
        {
            // The queue used to be an .m3u8 next to the saved playlists. Reading it once, when the
            // table is empty, is what stops the move into the database from throwing away the queue
            // of anyone upgrading.
            tracks = playlistService.LoadLast();
            if (tracks.Count > 0)
            {
                musicPlaylistStore.Save(tracks);
            }
        }

        if (tracks.Count == 0)
        {
            return;
        }

        // The track that was playing when the app closed is cued again but not started: restoring a
        // session should show where the user was, not make noise at start-up. An index that no
        // longer fits the queue is dropped by the playlist itself.
        int currentIndex = int.TryParse(settingsService.Get(Settings.MusicCurrentIndex), out int saved)
            ? saved
            : -1;

        // Through the player rather than into the list: a panel built before this ran subscribes to
        // the player and to nothing else, so a silent swap would leave it drawing the empty queue it
        // was created with until some unrelated transport event repainted it.
        musicPlayerService.ReplacePlaylist(tracks, currentIndex);
    }
}
