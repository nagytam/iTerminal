using iConsole.Commands;

namespace iTerminal.Core.Extensions;

/// <summary>A hot-key button a feature outside iTerminal.Core adds to the status bar's hot-key row.</summary>
public interface IStatusBarHotKey
{
    /// <summary>The button text, such as "Alt-F7-Stocks".</summary>
    string Label { get; }

    /// <summary>The columns the button takes in the row.</summary>
    int Width { get; }

    ICommand Command { get; }
}
