using iConsole.Display;
using iConsole.Drivers;
using iConsole.Graphics;
using iConsole.Inputs;

namespace iConsole.Services;

/// <summary>One terminal protocol the app can use, and whether it is in use on this terminal.</summary>
public readonly record struct TerminalProtocolState(string Name, bool IsEnabled);

/// <summary>
/// The terminal protocols the app speaks beyond plain characters and colours, each with the state the app has settled on for the
/// terminal it runs in: detected from the environment, switched off by an <c>ITERMINAL_*</c> variable, or (Sixel) switched off by
/// the setting. Shown on the splash dialog.
/// </summary>
public static class TerminalProtocols
{
    public static IReadOnlyList<TerminalProtocolState> Current() => Current(Environment.GetEnvironmentVariable, IsOutputRedirected());

    public static IReadOnlyList<TerminalProtocolState> Current(Func<string, string?> environment, bool outputRedirected) =>
    [
        new("Sixel graphics", TerminalGraphics.Current.IsSixelSupported),
        new("Sync output 2026", SynchronizedOutput.IsEnabledByEnvironment),
        new("Clipboard OSC 52", !outputRedirected),
        new("Hyperlinks OSC 8", AnsiRenderer.HyperlinksEnabled),
        new("Title OSC 2", !outputRedirected && environment("ITERMINAL_TERMINAL_TITLE") != "0"),
        new("Notify OSC 9/777", !outputRedirected && TerminalSequences.DetectNotificationStyle(environment) is TerminalNotificationStyle.Osc9 or TerminalNotificationStyle.Osc777),
        new("Pointer OSC 22", !outputRedirected && PointerShapeService.DetectSupport(environment)),
        new("Cursor shape", CursorShapes.IsEnabledByEnvironment),
        new("Focus reports", TerminalFocusReporting.IsEnabledByEnvironment),
        new("Kitty keyboard", !OperatingSystem.IsWindows() && KittyKeyboardProtocol.IsEnabledByEnvironment),
    ];

    private static bool IsOutputRedirected()
    {
        try
        {
            return Console.IsOutputRedirected;
        }
        catch (IOException)
        {
            return true;
        }
    }
}
