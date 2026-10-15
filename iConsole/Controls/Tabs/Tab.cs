using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls.Tabs;

public class Tab(IThemeService themeService) : Control(themeService)
{
    public ConcurrentList<TabPage> TabPages { get; set; } = [];

    private int _focusedTabPage;
    public int FocusedTabPage
    {
        get => _focusedTabPage;
        set
        {
            if (TabPages.Count == 0)
            {
                _focusedTabPage = 0;
                return;
            }

            _focusedTabPage = Math.Max(0, Math.Min(value, TabPages.Count - 1));
        }
    }

    /// <summary>The page whose content is on screen, or null while the tab has no pages.</summary>
    public TabPage? FocusedPage => TabPages.Count > 0 ? TabPages[FocusedTabPage] : null;

    /// <summary>
    /// The focused page's children, *not* the pages. It is what makes a tab transparent to layout,
    /// rendering and focus — but it also means the page is not in its own parent's child list, so
    /// nothing routes a key to <see cref="TabPage.HandleKeyEventAsync"/> the ordinary way. See
    /// <see cref="HandleKeyEventAsync"/>, which hands keys to the page rather than to this.
    /// </summary>
    public override ConcurrentList<IControl> Controls
    {
        get
        {
            if (TabPages.Count == 0)
                return [];
            return TabPages[FocusedTabPage].Controls;
        }
        internal set
        {
            if (TabPages.Count > 0)
                TabPages[FocusedTabPage].Controls = value;
        }
    }

    public override ConcurrentList<ICommand> Commands
    {
        get
        {
            if (TabPages.Count == 0)
                return [];
            return TabPages[FocusedTabPage].Commands;
        }
        internal set
        {
            if (TabPages.Count > 0)
                TabPages[FocusedTabPage].Commands = value;
        }
    }

    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    public void AddTabPage(TabPage tabPage)
    {
        tabPage.Parent = this;
        TabPages.Add(tabPage);

        if (TabPages.Count == 1)
        {
            tabPage.SetFocus();
        }
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        foreach (var tabPage in TabPages.AsSnapshot())
        {
            await tabPage.OnInitializeAsync();
        }

        if (TabPages.Count > 0)
        {
            FocusedTabPage = FocusedTabPage;
            for (var i = 0; i < TabPages.Count; i++)
            {
                if (i == FocusedTabPage)
                {
                    TabPages[i].SetFocus();
                }
                else
                {
                    TabPages[i].ResetFocus();
                }
            }
        }

        OnInitializeLayout();
    }

    public override void OnInitializeLayout()
    {
        if (TabPages.Count == 0)
        {
            base.OnInitializeLayout();
            return;
        }

        if (Orientation == Orientation.Horizontal)
        {
            foreach (var tabPage in TabPages.AsSnapshot())
            {
                tabPage.Position.X = 0;
                tabPage.Position.Y = 3;
                tabPage.ViewSize.Width = ViewSize.Width;
                tabPage.ViewSize.Height = Math.Max(0, ViewSize.Height - 3);
                tabPage.OnInitializeLayout();
            }
        }
        else if (Orientation == Orientation.Vertical)
        {
            var tabPages = TabPages.AsSnapshot();
            var tabStripWidth = tabPages.Max(tabPage => tabPage.Name.Length) + 6;
            foreach (var tabPage in tabPages)
            {
                tabPage.Position.X = tabStripWidth;
                tabPage.Position.Y = 0;
                tabPage.ViewSize.Width = Math.Max(0, ViewSize.Width - tabStripWidth);
                tabPage.ViewSize.Height = ViewSize.Height;
                tabPage.OnInitializeLayout();
            }
        }

        base.OnInitializeLayout();
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        if (TabPages.Count > 0)
        {
            await TabPages[FocusedTabPage].ShowAsync();
        }
    }

    /// <summary>
    /// Hides every page, not only the one showing. <see cref="Controls"/> returns the focused page's
    /// children, so the base implementation never reaches the others — and a page that is not
    /// showing can still own something on screen: a suggestion list is attached to the root.
    /// </summary>
    public override void Hide()
    {
        base.Hide();
        foreach (var tabPage in TabPages.AsSnapshot())
        {
            tabPage.Hide();
        }
    }

    /// <summary>
    /// Closes every page, for the reason <see cref="Hide"/> hides every page: closing the folder
    /// selector reached only the page that was showing, and the Path page's suggestion list — opened
    /// on the root while the dialog came up — stayed floating over the panels after it had gone.
    /// </summary>
    public override void Close()
    {
        base.Close();
        foreach (var tabPage in TabPages.AsSnapshot())
        {
            if (tabPage != FocusedPage)
            {
                tabPage.Close();
            }
        }
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (TabPages.Count == 0)
        {
            return;
        }

        if (Orientation == Orientation.Horizontal)
        {
            DrawHorizontal(buffer);
        }
        else if (Orientation == Orientation.Vertical)
        {
            DrawVertical(buffer);
        }

        TabPages[FocusedTabPage].Draw(buffer);
    }

    public void DrawHorizontal(IBuffer buffer)
    {
        var consoleColor = ThemeService.CurrentColorScheme;
        var line1 = Border.TopLeft + Border.Horizontal.Repeat((int)ViewSize.Width - 2) + Border.TopRight;
        ConxelString line2 = new(Border.Vertical, consoleColor.Foreground, consoleColor.Background);
        var tabPages = TabPages.AsSnapshot();
        for (int i = 0; i < tabPages.Count; i++)
        {
            var tabPage = tabPages[i];
            tabPage.GetColor(out var foregroundColor, out var backgroundColor);
            var tabName = new ConxelString();
            if (i != FocusedTabPage && i > 0 && i - 1 != FocusedTabPage)
            {
                // The tab before this one already drew the divider between them; drawing another
                // made "│ Scientific ││ Programming │". The cell keeps its width (name + 4) —
                // GetTabStripPageAt maps clicks with that arithmetic — by ending in a space.
                tabName.Append(" " + tabPage.Name + " " + Border.VerticalSingle + " ",
                    foregroundColor, backgroundColor);
            }
            else if (i != FocusedTabPage)
            {
                tabName.Append(Border.VerticalSingle + " " + tabPage.Name + " " + Border.VerticalSingle,
                    foregroundColor, backgroundColor);
            }
            else
            {
                tabName.Append(Border.OpenToLeft + " " + tabPage.Name + " " + Border.OpenToRight,
                    foregroundColor, backgroundColor);
            }
            tabName.HighlightHotKey(tabPage.HotConsoleKey, tabPage.HotKeyChar, Color.Green, Color.Blue);
            line2.Append(tabName);
        }
        line2.Expand((int)ViewSize.Width - 1, consoleColor.Foreground, consoleColor.Background);
        line2.Append(Border.Vertical, consoleColor.Foreground, consoleColor.Background);
        var line3 = Border.BottomLeft + Border.Horizontal.Repeat((int)ViewSize.Width - 2) + Border.BottomRight;
        var line1String = new ConxelString(line1, Color.White, Color.Blue);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line1String);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + 1), ViewSize, line2);
        var line3String = new ConxelString(line3, Color.White, Color.Blue);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + 2), ViewSize, line3String);
    }

    public void DrawVertical(IBuffer buffer)
    {
        var consoleColor = ThemeService.CurrentColorScheme;
        var tabPages = TabPages.AsSnapshot();
        var maxWidth = tabPages.Max(tabPage => tabPage.Name.Length) + 4;
        var borderLineString = Border.TopLeft + Border.Horizontal.Repeat(maxWidth) + Border.TopRight;
        ConxelString borderLine = new(borderLineString, consoleColor.Foreground, consoleColor.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y), ViewSize, borderLine);
        borderLineString = Border.MiddleLeft + Border.Horizontal.Repeat(maxWidth) + Border.MiddleRight;
        borderLine = new(borderLineString, consoleColor.Foreground, consoleColor.Background);
        int y = 1;
        var maxTabWidth = tabPages.Max(p => p.Name.Length);
        for (int i = 0; i < tabPages.Count; i++)
        {
            var tabPage = tabPages[i];
            tabPage.GetColor(out var foregroundColor, out var backgroundColor);
            var tabName = new ConxelString();
            tabName.Append(Border.Vertical, consoleColor.Foreground, consoleColor.Background);
            if (i != FocusedTabPage)
            {
                tabName.Append("  " + tabPage.Name.ExpandRight(maxTabWidth) + "  ",
                    foregroundColor, backgroundColor);
            }
            else
            {
                tabName.Append(Border.OpenToLeft + " " + tabPage.Name.ExpandRight(maxTabWidth) + " " + Border.OpenToRight,
                    foregroundColor, backgroundColor);
            }
            tabName.Append(Border.Vertical, consoleColor.Foreground, consoleColor.Background);
            tabName.HighlightHotKey(tabPage.HotConsoleKey, tabPage.HotKeyChar, Color.Green, Color.Blue);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + y), ViewSize, tabName);
            y++;
            if (i == tabPages.Count - 1)
            {
                borderLineString = Border.BottomLeft + Border.Horizontal.Repeat(maxWidth) + Border.BottomRight;
                borderLine = new(borderLineString, consoleColor.Foreground, consoleColor.Background);
            }
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + y), ViewSize, borderLine);
            y++;
        }
        while (y < ViewSize.Height)
        {
            borderLineString = Border.Vertical + " ".Repeat(maxWidth) + Border.Vertical;
            borderLine = new(borderLineString, consoleColor.Foreground, consoleColor.Background);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + y), ViewSize, borderLine);
            y++;
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (TabPages.Count > 1)
        {
            var step = GetPageSwitchStep(keyEvent);
            if (step != 0)
            {
                // Ctrl+arrow belongs to the tab strip unconditionally; a *plain* arrow is offered to
                // the focused control first. Taking it unconditionally is what made a vertical tab
                // swallow Up/Down before the focused list ever saw them — the list could not be
                // scrolled at all — and a horizontal one do the same to Left/Right in a text box.
                if (!keyEvent.Control && await HandleFocusedControlKeyEventAsync(keyEvent))
                {
                    StateHasChanged();
                    return true;
                }
                SwitchToPage(FocusedTabPage + step);
                return true;
            }
        }

        var focusedPage = FocusedPage;
        if (focusedPage is null)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        // The key goes to the *page*, not to the flattened Controls the base would walk. Because
        // Controls is overridden to return the focused page's children, base.HandleKeyEventAsync
        // reaches straight past the page into its list boxes and buttons, and a TabPage that
        // overrides HandleKeyEventAsync — to claim a key ahead of its own children, which is the
        // only reason to override it — was never called at all. That is what left Enter on a folder
        // selector list ticking a row's selection flag instead of going to the folder.
        //
        // The page's children, its hot key and its commands are all reached through the page, so
        // this covers everything the base did; only the Tab's own Ctrl+<number> page jump has to
        // stay here, and it stays where it was: after the focused page has declined the key.
        if (focusedPage.IsFocused && await focusedPage.HandleKeyEventAsync(keyEvent))
        {
            StateHasChanged();
            return true;
        }
        if (IsEnabled && await HandleHotKeyEventAsync(keyEvent))
        {
            return true;
        }
        if (!focusedPage.IsFocused && await focusedPage.HandleKeyEventAsync(keyEvent))
        {
            StateHasChanged();
            return true;
        }
        return false;
    }

    /// <summary>
    /// -1 for the "previous page" key, +1 for the "next page" key, 0 for anything else. The pair
    /// follows <see cref="Orientation"/>: a vertical tab strip reads Up/Down, a horizontal one
    /// Left/Right. Ctrl is allowed (and is the always-wins form); Alt and Shift are not.
    /// </summary>
    private int GetPageSwitchStep(KeyEvent keyEvent)
    {
        if (keyEvent.Alt || keyEvent.Shift)
        {
            return 0;
        }
        var previousKey = Orientation == Orientation.Horizontal ? ConsoleKey.LeftArrow : ConsoleKey.UpArrow;
        var nextKey = Orientation == Orientation.Horizontal ? ConsoleKey.RightArrow : ConsoleKey.DownArrow;
        if (keyEvent.ConsoleKey == previousKey)
        {
            return -1;
        }
        if (keyEvent.ConsoleKey == nextKey)
        {
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// Offers the key to the focused control only — deliberately not <see cref="Control.HandleKeyEventAsync"/>,
    /// whose fallback pass hands the key to *every* control. An unfocused list on the same page
    /// would otherwise consume the arrow and the tab strip could never be reached with one.
    /// </summary>
    private async Task<bool> HandleFocusedControlKeyEventAsync(KeyEvent keyEvent)
    {
        // Through the page, for the same reason HandleKeyEventAsync goes through it: a page that
        // claims a key ahead of its children has to be asked before its children are.
        var focusedPage = FocusedPage;
        if (focusedPage is not null && focusedPage.IsFocused)
        {
            return await focusedPage.HandleKeyEventAsync(keyEvent);
        }
        foreach (var control in Controls.AsSnapshot().Where(c => c.IsFocused))
        {
            if (await control.HandleKeyEventAsync(keyEvent))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A click on a page's name in the tab strip goes to that page, and leaves the cursor in the
    /// page's own content rather than on the strip — <see cref="SwitchToPage"/> focuses the page,
    /// and a <see cref="TabPage"/> cannot hold focus itself, so it hands it to the first control
    /// inside. Clicking the page already showing is therefore also the way back into its content
    /// from wherever the focus had wandered.
    ///
    /// The strip is drawn by this control rather than built out of child controls, so there is
    /// nothing under the pointer to route to: without this, a click on a page name reached the Tab
    /// and was passed straight down to the *current* page's children, which do not cover that part
    /// of the screen, and nothing happened at all.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.LeftClicked)
        {
            var clickedPage = GetTabStripPageAt(mouseEvent.Position);
            if (clickedPage >= 0)
            {
                SwitchToPage(clickedPage);
                return true;
            }
        }
        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>
    /// The page whose name is drawn under <paramref name="position"/>, or -1 for anywhere else —
    /// including the rules between the names on a vertical strip, which stand for no page. Read off
    /// the same arithmetic <see cref="DrawVertical"/> and <see cref="DrawHorizontal"/> lay the names
    /// out with; the two must be changed together.
    /// </summary>
    private int GetTabStripPageAt(Position position)
    {
        var tabPages = TabPages.AsSnapshot();
        if (tabPages.Count == 0)
        {
            return -1;
        }

        var x = (int)position.X - (int)AbsolutePosition.X;
        var y = (int)position.Y - (int)AbsolutePosition.Y;

        if (Orientation == Orientation.Vertical)
        {
            // Name rows alternate with rules, starting one row below the top border. The strip is
            // as wide as the widest name plus its padding and both borders — the same figure
            // OnInitializeLayout starts the pages after.
            var stripWidth = tabPages.Max(tabPage => tabPage.Name.Length) + 6;
            if (x < 0 || x >= stripWidth)
            {
                return -1;
            }
            var row = y - 1;
            if (row < 0 || row % 2 != 0)
            {
                return -1;
            }
            var index = row / 2;
            return index < tabPages.Count ? index : -1;
        }

        // Horizontal: a single row of names between the two border rows, each name taking its own
        // length plus a space and a divider on either side.
        if (y != 1)
        {
            return -1;
        }
        var left = 1;
        for (var i = 0; i < tabPages.Count; i++)
        {
            var width = tabPages[i].Name.Length + 4;
            if (x >= left && x < left + width)
            {
                return i;
            }
            left += width;
        }
        return -1;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (TabPages.Count <= 1)
        {
            yield break;
        }
        var previous = Orientation == Orientation.Horizontal ? "Left" : "Up";
        var next = Orientation == Orientation.Horizontal ? "Right" : "Down";
        yield return ($"Ctrl+{previous} / Ctrl+{next}", "Switch to the previous / next tab page.");
        yield return ($"{previous} / {next}", "Switch tab pages when the focused control does not use the arrow keys.");
        yield return ("Ctrl+<number>", "Jump straight to the tab page carrying that number in its name.");
    }

    /// <summary>
    /// Shows page <paramref name="newIndex"/> and <b>puts the cursor on its first control</b> — the
    /// page cannot hold focus itself, so <see cref="Control.SetFocus"/> hands it to the first focusable
    /// control inside, which is why every page lists its list or its text box before its buttons. The
    /// other pages are reset explicitly rather than left to <c>root.ResetFocus()</c>: <see cref="Controls"/>
    /// returns only the focused page's children, so a page that is no longer showing is not reachable
    /// from the root at all and would keep the focus flag it was left with.
    ///
    /// Switching page is also what takes a suggestion list off the screen. A <see cref="Popup"/>
    /// attaches itself to the root rather than to the control that opened it, so leaving the page it
    /// belongs to does not hide it, and it would float over whichever page arrives in its place.
    /// </summary>
    private void SwitchToPage(int newIndex)
    {
        if (newIndex < 0 || newIndex >= TabPages.Count)
        {
            return;
        }
        var leavingPage = FocusedPage;
        if (leavingPage is not null && leavingPage != TabPages[newIndex])
        {
            ITransientOverlayHost.TryCloseInnermostOverlay(leavingPage);
        }
        FocusedTabPage = newIndex;
        for (var j = 0; j < TabPages.Count; j++)
        {
            if (j == FocusedTabPage)
            {
                TabPages[j].SetFocus();
            }
            else
            {
                TabPages[j].ResetFocus();
            }
        }
        StateHasChanged();
    }

    /// <summary>
    /// Ctrl and the number in a page's name jumps to that page. It goes through
    /// <see cref="SwitchToPage"/> unconditionally — including for the page already showing, which is
    /// then the keyboard's way back into the page's content from wherever the focus had wandered, the
    /// same gesture as clicking the name of the page already up. Read as "only when that page is not
    /// already focused", as it used to be, the key did nothing at all once the cursor had moved onto
    /// one of the page's buttons: the page was focused, so the jump was skipped, and there was no key
    /// left that brought the cursor back to the list.
    /// </summary>
    public override async Task<bool> HandleHotKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = false;
        if (keyEvent.Control)
        {
            int i = 0;
            foreach (var tabPage in TabPages)
            {
                if (keyEvent.KeyChar == tabPage.HotKeyChar ||
                    keyEvent.ConsoleKey == tabPage.HotConsoleKey)
                {
                    isHandled = true;
                    SwitchToPage(i);
                }
                i++;
            }
        }
        isHandled = isHandled || await base.HandleHotKeyEventAsync(keyEvent);
        return isHandled;
    }
}
