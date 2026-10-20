using iConsole.Buffers;
using iConsole.Data;
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

    /// <summary>
    /// Makes the bar usable with the mouse. A click on a title opens its drop-down (or closes it when it is
    /// already open, or switches from the one that is), a click on an entry of an open drop-down runs it, and
    /// a click anywhere else closes the menu — and is swallowed, so it does not also act on what is beneath.
    /// With no menu open, clicks off the titles go on to the bar's other children.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (!mouseEvent.LeftClicked)
        {
            return await base.HandleMouseEventAsync(mouseEvent);
        }

        var chain = new List<MenuItem>();
        for (var open = OpenChild; open is not null; open = open.OpenChild)
        {
            chain.Add(open);
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            if (await chain[i].HandleDropDownClickAsync(mouseEvent))
            {
                return true;
            }
        }

        if (mouseEvent.Position.Y == AbsolutePosition.Y && mouseEvent.Position.X >= AbsolutePosition.X)
        {
            var column = (int)(mouseEvent.Position.X - AbsolutePosition.X);
            var start = 0;
            foreach (var title in MenuItems)
            {
                if (column >= start && column < start + title.Text.Length)
                {
                    var wasOpen = title.IsOpened;
                    CloseMenu();
                    if (!wasOpen && title.Text != "-")
                    {
                        title.OpenMenu();
                    }

                    return true;
                }

                start += title.Text.Length + 1;
            }
        }

        if (chain.Count > 0)
        {
            CloseMenu();
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
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
