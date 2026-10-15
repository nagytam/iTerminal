using iTerminal.Core.Data.Music;

namespace iTerminal.Core.Services.Music;

/// <summary>
/// The music player's whole public surface: transport, playlist cursor, volume and output device.
/// Every method returns immediately — decoding and device I/O happen on a private pump thread — so
/// key handlers stay inside the app's 200 ms budget even when a file takes a moment to open.
/// </summary>
public interface IMusicPlayerService : IDisposable
{
    /// <summary>The tracks and the cursor. Mutating it while playing is safe.</summary>
    Playlist Playlist { get; }

    PlaybackState State { get; }

    /// <summary>The loaded track, or null when stopped.</summary>
    MusicTrack? CurrentTrack { get; }

    /// <summary>Elapsed time of the audio actually reaching the speakers.</summary>
    TimeSpan Position { get; }

    /// <summary>Length of the current track, or <see cref="TimeSpan.Zero"/> when unknown.</summary>
    TimeSpan Duration { get; }

    /// <summary>Output level, 0–100.</summary>
    int Volume { get; set; }

    RepeatMode RepeatMode { get; set; }

    bool IsShuffled { get; set; }

    /// <summary>Id of the chosen output device; null or empty means the system default.</summary>
    string? DeviceId { get; }

    /// <summary>Last error or notice for the status line, e.g. a missing file or absent ffmpeg.</summary>
    string StatusMessage { get; }

    /// <summary>True when the last status message reports a failure rather than progress.</summary>
    bool StatusIsError { get; }

    /// <summary>Raised from the pump thread whenever visible state changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Replaces the whole queue and tells the views about it. <see cref="Playlist"/> is a plain data
    /// type with no events of its own — every other change to it comes from the pump, which raises
    /// <see cref="StateChanged"/> afterwards anyway — so a queue restored from the database has to
    /// come through here, or the panel keeps drawing the empty list it was built with.
    ///
    /// <para><paramref name="currentIndex"/> cues a track without playing it, which is how a
    /// restored session shows what was last played. It is part of this call rather than a separate
    /// assignment because replacing the list resets the cursor, and setting it afterwards would
    /// announce a queue with nothing cued and then change it silently.</para>
    /// </summary>
    void ReplacePlaylist(IEnumerable<MusicTrack> tracks, int currentIndex = -1);

    /// <summary>Starts the playlist entry at <paramref name="index"/>. Out-of-range indexes stop playback.</summary>
    void PlayIndex(int index);

    /// <summary>Plays if stopped, otherwise flips between playing and paused.</summary>
    void TogglePlayPause();

    void Pause();

    void Resume();

    void Stop();

    /// <summary>Advances per <see cref="RepeatMode"/> and shuffle; stops at the end when not repeating.</summary>
    void Next();

    /// <summary>
    /// Restarts the current track when more than a few seconds in, otherwise steps back — the
    /// behaviour every other music player has trained users to expect.
    /// </summary>
    void Previous();

    /// <summary>Jumps to an absolute position in the current track.</summary>
    void Seek(TimeSpan position);

    /// <summary>Jumps forward or backward relative to the current position.</summary>
    void SeekRelative(TimeSpan offset);

    /// <summary>Switches output device, restarting the current track at its current position.</summary>
    void SetDevice(string? deviceId);
}
