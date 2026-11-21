using System.CommandLine;
using System.CommandLine.Help;
using iTerminal.Core.Services.Elevation;
using iTerminal.Core.Services.Updates;

namespace iTerminal.Core.Application;

/// <summary>
/// The switches iTerminal reads at start-up, parsed with System.CommandLine. An unknown switch is not fatal, so the
/// interface still starts, but it is reported (see <see cref="UnknownSwitches"/>) so that a mistyped switch such as
/// <c>--udpate</c> is not silently ignored. Words that are not switches are ignored.
/// </summary>
public sealed class LaunchOptions
{
    private readonly Option<bool> _update = new(UpdateCommandLine.Argument)
    {
        Description = "Installs the newest iTerminal release without starting the interface, then exits.",
    };

    private readonly Option<string?> _waitForParent = new(ElevationService.WaitForParentArgument)
    {
        Description = "Waits for the process with this id to exit before starting (used by the elevation relaunch).",
        Hidden = true,
    };

    private readonly ParseResult _result;
    private readonly HelpOption _help;

    private LaunchOptions(string[] args)
    {
        var root = new RootCommand(Introduction);
        root.Options.Add(_update);
        root.Options.Add(_waitForParent);
        _help = root.Options.OfType<HelpOption>().Single();
        _result = root.Parse(args);
    }

    /// <summary>What <c>--help</c> says first: the app in a few lines, before the switches.</summary>
    public const string Introduction =
        "iTerminal is a two-panel file manager for the terminal. It browses folders, archives, " +
        "remote servers and drives side by side, runs commands and opens files, all from the keyboard. " +
        "Run it without arguments to start the interface.";

    /// <summary>Reads the switches from the command line.</summary>
    public static LaunchOptions Parse(string[] args) => new(args);

    /// <summary>True when <c>--update</c> was given.</summary>
    public bool Update => _result.GetValue(_update);

    /// <summary>True when <c>--help</c>, <c>-h</c> or <c>-?</c> was given: the help is shown instead of starting the app.</summary>
    public bool HelpRequested => _result.GetResult(_help) is not null;

    /// <summary>Writes the help (the introduction and the switches) to the console and returns the exit code.</summary>
    public int ShowHelp() => _result.Invoke();

    /// <summary>The tokens that start with <c>-</c> and match no switch, in the order given.</summary>
    public IReadOnlyList<string> UnknownSwitches =>
        _result.UnmatchedTokens.Where(token => token.StartsWith('-')).ToList();

    /// <summary>The process id given to <c>--wait-for-parent</c>, when it was given with a valid number.</summary>
    public int? ParentProcessId => int.TryParse(_result.GetValue(_waitForParent), out int id) ? id : null;
}
