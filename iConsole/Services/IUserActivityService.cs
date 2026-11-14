namespace iConsole.Services;

/// <summary>
/// When the person last used the keyboard or clicked the mouse. The screen saver starts once this has been quiet for long
/// enough. Pointer movement without a button does not count: a mouse left on the desk is not someone at the screen.
/// </summary>
public interface IUserActivityService
{
    /// <summary>Records activity now. Called by the input consumers for every key press and every click.</summary>
    void NoteActivity();

    /// <summary>The time since the last <see cref="NoteActivity"/>, or since the service was made if there has been none.</summary>
    TimeSpan IdleFor { get; }
}
