using iConsole.Controls;

namespace iTerminal.Core.Extensions;

/// <summary>
/// A status indicator that a feature outside iTerminal.Core adds to the menu bar, to the left of the clock. The bar
/// places the indicator's parts together, or hides them all when the parts do not fit in the room the bar has left
/// or when <see cref="IsEnabled"/> is false. Indicators are placed in ascending <see cref="Order"/>, after the bar's
/// own online, task and music indicators, so when the bar is short the highest-numbered indicator gives way first.
/// </summary>
public interface IMenuBarIndicator
{
    int Order { get; }

    /// <summary>Read on every idle pass, so a change in the settings dialog applies at once.</summary>
    bool IsEnabled { get; }

    /// <summary>The controls that are placed together, each with the columns it takes.</summary>
    IReadOnlyList<MenuBarIndicatorPart> Parts { get; }
}

/// <summary>One control of a menu bar indicator, with the columns it takes.</summary>
public sealed record MenuBarIndicatorPart(Control Control, int Width);
