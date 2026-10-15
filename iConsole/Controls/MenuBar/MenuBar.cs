using iConsole.Buffers;
using iConsole.Services;
using System.Diagnostics;

namespace iConsole.Controls;

[DebuggerDisplay("{Name} {AbsolutePosition} {ViewSize}")]
public class MenuBar : MenuItem
{
    public MenuBar(IEnumerable<MenuItem> menuItems, IThemeService themeService)
        : base("MenuBar", null, menuItems, themeService)
    {
        Name = nameof(MenuBar);
        IsVisible = true;
    }

    public override void OnInitializeZOrder()
    {
        base.OnInitializeZOrder();
        ZOrder = 0;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
    }

    /// <summary>
    /// Keeps <see cref="Control.IsModal"/> in sync with whether any top-level menu is
    /// currently open. While modal, <c>Desktop.GetModalChildControl()</c> routes every key
    /// event straight into the menu bar instead of the focused panel, which is what makes
    /// keyboard navigation of an open menu possible.
    /// </summary>
    public void SyncModalState()
    {
        IsModal = MenuItems.Any(m => m.IsOpened);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        foreach (var item in MenuItems)
        {
            var letter = item.HotKeyChar?.ToString() ?? item.HotConsoleKey?.ToString();
            if (!string.IsNullOrEmpty(letter))
            {
                yield return ($"Ctrl+{letter}", $"Open {item.Text} menu");
            }
        }
        yield return ("Up/Down", "Move menu selection");
        yield return ("Right/Enter", "Open submenu / run command");
        yield return ("Left", "Back / previous menu");
        yield return ("Esc", "Close menu");
    }
}
