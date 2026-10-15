namespace iTerminal.Core.Model;

/// <summary>
/// A panel whose terminal commands do not run on this machine, and which says where they do run:
/// the terminal draws <see cref="TerminalPrompt"/> in place of "Terminal" (<c>web:/etc&gt;</c> for a
/// panel inside a Docker container), so a command is never typed believing it runs somewhere else.
/// </summary>
public interface ITerminalPromptSource
{
    /// <summary>Where a command would run now, or null for the ordinary prompt.</summary>
    string? TerminalPrompt { get; }
}
