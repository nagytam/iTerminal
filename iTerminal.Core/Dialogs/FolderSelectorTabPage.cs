using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Base for the pages of <see cref="FolderSelectorDialog"/>. Every page answers the same question —
/// "which folder do you want?" — so every page answers it with the same three keys: the arrows move,
/// Enter goes, Esc cancels.
///
/// Whichever page is up, its <b>first control holds the cursor</b> the moment the page is selected —
/// a page cannot hold focus itself, so <c>Tab.SwitchToPage</c> hands it to the first focusable control
/// inside. That is why every page adds its list or its text box before its buttons: a page that opens
/// with a button focused answers the arrow keys with nothing and Enter with the wrong verb.
///
/// Enter is the part that needs a base class. Each page's real work lives in a command attached to
/// its "Select" button, but the control that actually holds focus is the list, and
/// <see cref="iConsole.Controls.ListBox{T}"/> binds Enter to its own <c>SelectListItemCommand</c>,
/// which reports the key handled unconditionally. Routed the ordinary way — focused control first —
/// Enter therefore ticked a row's selection flag and stopped, and the page's select command was
/// reachable only by tabbing onto the button. Claiming Enter here, ahead of the children, is what
/// makes the obvious key do the obvious thing.
/// </summary>
public abstract class FolderSelectorTabPage : TabPage
{
    protected FolderSelectorTabPage(string name, ConsoleKey hotConsoleKey, IThemeService themeService)
        : base(name, themeService)
    {
        HotConsoleKey = hotConsoleKey;
    }

    /// <summary>
    /// The command Enter runs from anywhere on this page. Null while the page is still being
    /// constructed, and for a page that has nothing to select.
    /// </summary>
    protected abstract ICommand? SelectCommand { get; }

    /// <summary>What this page's Enter does, for the F1 help. One line.</summary>
    protected abstract string SelectDescription { get; }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // A focused Button keeps Enter for itself: "Add to favourites" under the cursor has to add,
        // not navigate away. Every other focused control (the lists, the text boxes) yields it.
        if (IsSelectKey(keyEvent) && GetFocusedControl() is not Button)
        {
            var selectCommand = SelectCommand;
            if (selectCommand is not null && await selectCommand.RunAsync(null))
            {
                return true;
            }
        }
        return await base.HandleKeyEventAsync(keyEvent);
    }

    private static bool IsSelectKey(KeyEvent keyEvent) =>
        keyEvent.ConsoleKey == ConsoleKey.Enter
        && !keyEvent.Control
        && !keyEvent.Alt
        && !keyEvent.Shift;

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", SelectDescription);
        yield return ("Tab / Shift+Tab", "Move between this page's list and its buttons.");
        yield return ("Esc", "Dismiss the suggestion list if one is up, otherwise close the folder selector without navigating.");
        yield return ("F1", "Show this help.");
    }
}
