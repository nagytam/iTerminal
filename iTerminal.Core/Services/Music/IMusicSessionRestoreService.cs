namespace iTerminal.Core.Services.Music;

/// <summary>
/// Puts the session the last run saved — volume, output device, repeat/shuffle and the playing queue
/// — back into the player.
///
/// <para>It is a service rather than a step inside <see cref="Jobs.MusicSessionJob"/> because two
/// unrelated things need it and neither can wait for the other: the job, so the queue is restored
/// whether or not a music panel is ever opened, and <see cref="Model.ContentModels
/// .ContentModelMusicPlaylist"/>, so a panel built before the jobs have started draws the saved queue
/// on its first paint instead of an empty list. Whoever gets there first does the work.</para>
///
/// <para>That is also why it happens exactly once per run. After the queue has been read, memory is
/// the truth: a user who clears the queue and reopens the panel a second later must not have it
/// handed back by a table that has not been written yet.</para>
/// </summary>
public interface IMusicSessionRestoreService
{
    /// <summary>
    /// Restores the saved session if that has not happened yet. Returns true when this call is the
    /// one that did it. Never throws — a session that cannot be read is not worth failing over.
    /// </summary>
    bool RestoreOnce();
}
