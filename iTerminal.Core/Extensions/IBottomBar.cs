using iConsole.Controls;

namespace iTerminal.Core.Extensions;

/// <summary>
/// The bar under the two panels and the terminal, which the content lays out and gives focus to. Core does not know what
/// it is; a feature outside Core (the AI assistant) supplies it through <see cref="IBottomBarContribution"/>.
/// </summary>
public interface IBottomBar : IControl
{
    /// <summary>The rows the bar wants, given the configured height. It may ask for fewer when it is collapsed.</summary>
    int PreferredHeight(int configuredHeight);

    /// <summary>Raised when <see cref="PreferredHeight"/> changes, so the content lays out again.</summary>
    event EventHandler? PreferredHeightChanged;
}
