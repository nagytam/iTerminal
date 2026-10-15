namespace iConsole.Diagnostics;

/// <summary>
/// A single recorded execution attempt of an <see cref="Commands.ICommand"/>, captured by
/// whichever <see cref="ICommandLogger"/> is attached to the command at the time it runs.
/// </summary>
public sealed record CommandExecutionRecord(
    DateTime Timestamp,
    string CommandName,
    string? ControlName,
    object? Args,
    bool CanExecute,
    bool Executed,
    string? ExceptionMessage,
    TimeSpan Duration);
