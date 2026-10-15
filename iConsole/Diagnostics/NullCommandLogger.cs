namespace iConsole.Diagnostics;

/// <summary>
/// No-op implementation of <see cref="ICommandLogger"/> used when no logger has been injected
/// (the production default). Allocation-free.
/// </summary>
public sealed class NullCommandLogger : ICommandLogger
{
    public static readonly NullCommandLogger Instance = new();

    private NullCommandLogger()
    {
    }

    public void Log(CommandExecutionRecord record)
    {
    }
}
