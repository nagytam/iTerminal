namespace iConsole.Services;

/// <summary>The title of the terminal window or tab the application runs in.</summary>
public interface ITerminalTitleService
{
    /// <summary>The title last set, or an empty string when the application has not set one.</summary>
    string Title { get; set; }

    /// <summary>Puts back the title the terminal had before the application first set one. Safe to call when none was set.</summary>
    void Restore();
}
