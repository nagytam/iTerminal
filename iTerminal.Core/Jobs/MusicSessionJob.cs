using iConsole.Jobs;
using iTerminal.Core.Data;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using System.Globalization;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Restores the player's session at start-up (volume, output device, repeat/shuffle and the playing
/// queue) and writes it back as it changes. Doing this in a job rather than in a dialog means the
/// queue survives a restart whether or not the user ever opens the player, and keeps the database
/// I/O off both the render thread and the key handler.
/// </summary>
public class MusicSessionJob(
    IMusicPlayerService musicPlayerService,
    IMusicPlaylistStore musicPlaylistStore,
    IMusicSessionRestoreService musicSessionRestoreService,
    ISettingsService settingsService) : Job
{
    /// <summary>
    /// How often the session is checked for changes. The player has no "session changed" event and
    /// does not want one — the pump would be raising it from inside playback — so this polls a
    /// version stamp and a signature instead, and only writes when one of them moved.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    private readonly IMusicPlayerService _musicPlayerService = musicPlayerService;
    private readonly IMusicPlaylistStore _musicPlaylistStore = musicPlaylistStore;
    private readonly IMusicSessionRestoreService _musicSessionRestoreService = musicSessionRestoreService;
    private readonly ISettingsService _settingsService = settingsService;

    private int _savedPlaylistVersion = -1;
    private string _savedSettingsSignature = string.Empty;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        Restore();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                PersistIfChanged();
            }
            catch
            {
                // Session persistence is a convenience; never let it stop the job.
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Final write so a clean exit does not lose the last few seconds of changes.
        try
        {
            PersistIfChanged();
        }
        catch
        {
        }
    }

    /// <summary>
    /// Restores the saved session unless a music panel opened early enough to have done it already,
    /// then takes the result as the baseline so the first poll does not write back what was just
    /// read. The baseline is captured either way — what matters is that it reflects the restored
    /// session, not who performed the restore.
    /// </summary>
    private void Restore()
    {
        try
        {
            _musicSessionRestoreService.RestoreOnce();
        }
        catch
        {
            // A corrupt saved session must not stop the app from starting.
        }

        _savedPlaylistVersion = _musicPlayerService.Playlist.Version;
        _savedSettingsSignature = BuildSettingsSignature();
    }

    private void PersistIfChanged()
    {
        var playlist = _musicPlayerService.Playlist;
        var version = playlist.Version;
        if (version != _savedPlaylistVersion)
        {
            _musicPlaylistStore.Save(playlist.Tracks);
            _savedPlaylistVersion = version;
        }

        var signature = BuildSettingsSignature();
        if (signature != _savedSettingsSignature)
        {
            _settingsService.Set(Settings.MusicVolume, _musicPlayerService.Volume.ToString(CultureInfo.InvariantCulture));
            _settingsService.Set(Settings.MusicOutputDeviceId, _musicPlayerService.DeviceId ?? string.Empty);
            _settingsService.Set(Settings.MusicRepeatMode, _musicPlayerService.RepeatMode.ToString());
            _settingsService.Set(Settings.MusicShuffle, _musicPlayerService.IsShuffled.ToString());
            _settingsService.Set(
                Settings.MusicCurrentIndex,
                _musicPlayerService.Playlist.CurrentIndex.ToString(CultureInfo.InvariantCulture));
            _savedSettingsSignature = signature;
        }
    }

    /// <summary>
    /// Which cue is playing belongs here rather than with the queue: advancing a track moves it
    /// several times an hour while the queue's own version stamp never changes, so watching only the
    /// queue would save the session with whatever was cued when the list was last edited.
    /// </summary>
    private string BuildSettingsSignature() =>
        string.Join(
            '|',
            _musicPlayerService.Volume,
            _musicPlayerService.DeviceId ?? string.Empty,
            _musicPlayerService.RepeatMode,
            _musicPlayerService.IsShuffled,
            _musicPlayerService.Playlist.CurrentIndex);
}
