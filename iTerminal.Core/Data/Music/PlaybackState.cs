namespace iTerminal.Core.Data.Music;

/// <summary>Where the playback engine currently is. Drives the transport glyph in the player UI.</summary>
public enum PlaybackState
{
    /// <summary>Nothing loaded, or playback was stopped and the decoder released.</summary>
    Stopped = 0,

    /// <summary>Audio is being pumped to the output device.</summary>
    Playing = 1,

    /// <summary>A track is loaded and positioned, but the pump is idle.</summary>
    Paused = 2,
}

/// <summary>What happens when the playlist reaches its end, or a single track finishes.</summary>
public enum RepeatMode
{
    /// <summary>Stop after the last track.</summary>
    Off = 0,

    /// <summary>Wrap around to the first track after the last one.</summary>
    All = 1,

    /// <summary>Replay the current track forever until the user moves on.</summary>
    One = 2,
}
