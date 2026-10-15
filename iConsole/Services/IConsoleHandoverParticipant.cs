namespace iConsole.Services;

/// <summary>
/// Something that has put the terminal into a state of its own — raw input, mouse reporting, hidden
/// cursor, colours — and has to undo it while <see cref="ConsoleOwnership"/> lends the terminal to a
/// child process, then redo it when the terminal comes back.
///
/// <para>Both are called with the terminal held exclusively: no participant's loop is between
/// <see cref="ConsoleOwnership.TryBeginUse"/> and <see cref="ConsoleOwnership.EndUse"/>, so neither
/// races the work it suspends. The defaults do nothing, for a driver with no state to undo (the
/// virtual terminal the tests draw on).</para>
/// </summary>
public interface IConsoleHandoverParticipant
{
    /// <summary>Puts the terminal back the way a program started from a shell expects it.</summary>
    void OnConsoleReleased()
    {
    }

    /// <summary>Takes the terminal back after the child has exited; whatever the child left on it is the participant's to fix.</summary>
    void OnConsoleReacquired()
    {
    }
}
