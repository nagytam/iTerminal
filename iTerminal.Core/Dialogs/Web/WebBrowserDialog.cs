using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iTerminal.Core.Helper;
using iConsole.Services;
using iTerminal.Core.Model.Web;
using iTerminal.Core.Services.Web;

namespace iTerminal.Core.Dialogs.Web;

/// <summary>
/// The Web Browser: a full-screen, text-mode browser with tabs, an address bar, back/forward history,
/// bookmarks, find in page, forms, cookies, page source and saving. Pages are fetched by
/// <see cref="IWebFetcher"/> and laid out as text by <see cref="HtmlLayout"/> — there is no JavaScript and
/// no CSS, and images appear as their description.
///
/// <para>The dialog only draws and routes keys. Fetching and layout run on background tasks and land in
/// the tab under its lock (see <see cref="WebBrowserModel"/>), so no key handler or <see cref="Draw"/> waits
/// for the network; <see cref="Dialog.OnIdle"/> repaints what the tasks changed.</para>
/// </summary>
public class WebBrowserDialog : Dialog
{
    private enum Mode
    {
        Browse,
        Address,
        Find,
        Field,
        Bookmarks,
        History,
        Help,
    }

    private const int ChromeRows = 3;
    private const int MaxTabWidth = 22;

    private static readonly string[] HelpLines =
    [
        "Web Browser",
        "",
        "Navigation",
        "  Ctrl+L, F6          Address bar: type an address, a file path or words to search for",
        "  Alt+Left, Backspace Back              Alt+Right, Shift+Backspace  Forward",
        "  F5, Ctrl+R          Reload            Alt+Home                    Home page",
        "  Esc                 Stop loading; with nothing loading, close the browser",
        "",
        "Page",
        "  Up Down PgUp PgDn Home End, mouse wheel   Scroll",
        "  Tab, Shift+Tab      Next / previous link or form control",
        "  Enter               Follow the link, press the button, edit the text box, open the list",
        "  Ctrl+Enter          Open the link in a new tab",
        "  Space               Tick a box or choose a radio button; otherwise page down",
        "  Left, Right         Choose the previous / next option of a drop-down list",
        "  Ctrl+F, /           Find in page    F3, n  next match    Shift+F3, N  previous match",
        "  F9                  Show the page source / the rendered page",
        "  F7                  Save the page (or the file it is) to the download folder",
        "",
        "Tabs, bookmarks and history",
        "  Ctrl+T              New tab           Ctrl+W                      Close tab",
        "  Ctrl+PgDn, Ctrl+Tab Next tab          Ctrl+PgUp, Ctrl+Shift+Tab   Previous tab     Alt+1…9  Go to tab",
        "  Ctrl+D              Bookmark this page, or remove the bookmark",
        "  F2                  Bookmarks         F4                          History (Del removes an entry)",
        "",
        "In a text box",
        "  Type to edit; Enter sends the form; Tab moves on; Esc stops editing; Ctrl+U clears it.",
        "",
        "Not supported: JavaScript, CSS layout, images, plug-ins. Text is shown as it flows, tables as columns.",
        "",
        "Press any key to go back.",
    ];

    private readonly WebBrowserModel _model;
    private Mode _mode = Mode.Browse;
    private bool _started;
    private string _appliedTitle = string.Empty;

    private string _address = string.Empty;
    private int _addressCursor;
    private bool _addressReplace;

    private string _findText = string.Empty;
    private string _findQuery = string.Empty;

    private WebField? _editing;

    private List<WebBookmark> _list = [];
    private int _listIndex;
    private int _listScroll;
    private int _helpScroll;

    // Where things were drawn last, for the mouse. Written by Draw, read by the mouse handler: both on the UI thread.
    private readonly List<(int X0, int X1, int Index)> _tabHits = [];
    private (int X0, int X1) _back, _forward, _reload, _star, _newTab, _addressBox;
    private int _screenX, _screenY, _contentWidth, _contentHeight;

    public WebBrowserDialog(IWebFetcher fetcher, WebBrowserStore store, IThemeService themeService)
        : this(new WebBrowserModel(fetcher, store, DefaultDownloadDirectory()), themeService)
    {
    }

    internal WebBrowserDialog(WebBrowserModel model, IThemeService themeService)
        : base(nameof(WebBrowserDialog), themeService)
    {
        _model = model;
        Title = "Web Browser";
    }

    internal WebBrowserModel Model => _model;

    internal string Status { get; private set; } = string.Empty;

    /// <summary>True while text is being typed into the address bar, the find box or a form field.</summary>
    internal bool IsTyping => _mode is Mode.Address or Mode.Find or Mode.Field;

    internal bool ShowsBookmarks => _mode == Mode.Bookmarks;

    internal bool ShowsHistory => _mode == Mode.History;

    internal string AddressText => _address;

    internal int ViewHeight => Math.Max(1, (int)ViewSize.Height - 2 - ChromeRows);

    private static string DefaultDownloadDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    #region Lifecycle

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        if (!_started)
        {
            _started = true;
            var tab = _model.Active;
            if (tab.Current is null && !tab.IsLoading)
            {
                _ = _model.NavigateAsync(tab, _model.Store.Home);
            }
        }

        _mode = Mode.Browse;
        _editing = null;
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
        }

        base.OnInitializeLayout();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        var title = "Web Browser";
        if (_mode is Mode.Browse or Mode.Find or Mode.Field)
        {
            var tabTitle = _model.Active.Title;
            if (tabTitle.Length > 0 && tabTitle != "New tab" && tabTitle != "Web Browser")
            {
                title = "Web Browser — " + Shorten(tabTitle, Math.Max(10, (int)ViewSize.Width - 24));
            }
        }

        if (title != _appliedTitle)
        {
            _appliedTitle = title;
            Title = title;
            StateHasChanged();
        }
    }

    #endregion

    #region Keys

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        var handled = _mode switch
        {
            Mode.Address => HandleAddressKey(keyEvent),
            Mode.Find => HandleFindKey(keyEvent),
            Mode.Field => HandleFieldKey(keyEvent),
            Mode.Bookmarks or Mode.History => HandleListKey(keyEvent),
            Mode.Help => HandleHelpKey(keyEvent),
            _ => HandleBrowseKey(keyEvent),
        };

        if (handled)
        {
            StateHasChanged();
            return true;
        }

        // Anything the browser does not claim must not reach Dialog's Tab and Esc commands.
        if (keyEvent.ConsoleKey is ConsoleKey.Tab or ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Alt)
        {
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private bool HandleBrowseKey(KeyEvent key)
    {
        var tab = _model.Active;
        switch (key.ConsoleKey)
        {
            case ConsoleKey.Escape when !key.Control && !key.Alt && !key.Shift:
                // Esc stops a load that replaces a page; with nothing on screen yet there is nothing to keep, so it closes.
                if (tab.IsLoading && tab.Current is not null)
                {
                    _model.Stop(tab);
                }
                else if (_findQuery.Length > 0)
                {
                    _findQuery = string.Empty;
                }
                else
                {
                    Close();
                }

                return true;
            case ConsoleKey.L when key.Control && !key.Alt:
            case ConsoleKey.F6:
                BeginAddress();
                return true;
            case ConsoleKey.LeftArrow when key.Alt:
            case ConsoleKey.Backspace when !key.Shift && !key.Control && !key.Alt:
                if (!_model.GoBack(tab))
                {
                    _model.Say(tab, "There is no earlier page.");
                }

                return true;
            case ConsoleKey.RightArrow when key.Alt:
            case ConsoleKey.Backspace when key.Shift:
                if (!_model.GoForward(tab))
                {
                    _model.Say(tab, "There is no later page.");
                }

                return true;
            case ConsoleKey.Home when key.Alt:
                _ = _model.NavigateAsync(tab, _model.Store.Home);
                return true;
            case ConsoleKey.F5:
            case ConsoleKey.R when key.Control && !key.Alt:
                _ = _model.ReloadAsync(tab);
                return true;
            case ConsoleKey.T when key.Control && !key.Alt:
                NewTab();
                return true;
            case ConsoleKey.W when key.Control && !key.Alt:
                _model.CloseTab(_model.ActiveIndex);
                return true;
            case ConsoleKey.PageDown when key.Control:
            case ConsoleKey.Tab when key.Control && !key.Shift:
                _model.SelectNext(1);
                return true;
            case ConsoleKey.PageUp when key.Control:
            case ConsoleKey.Tab when key.Control && key.Shift:
                _model.SelectNext(-1);
                return true;
            case ConsoleKey.D1 or ConsoleKey.D2 or ConsoleKey.D3 or ConsoleKey.D4 or ConsoleKey.D5 or ConsoleKey.D6 or ConsoleKey.D7 or ConsoleKey.D8 or ConsoleKey.D9 when key.Alt && !key.Control:
                _model.SelectTab(key.ConsoleKey - ConsoleKey.D1);
                return true;
            case ConsoleKey.D when key.Control && !key.Alt:
                ToggleBookmark();
                return true;
            case ConsoleKey.F2:
                OpenList(Mode.Bookmarks);
                return true;
            case ConsoleKey.F4:
                OpenList(Mode.History);
                return true;
            case ConsoleKey.F1:
                _mode = Mode.Help;
                _helpScroll = 0;
                return true;
            case ConsoleKey.F7:
                Save();
                return true;
            case ConsoleKey.F9:
                ToggleSource();
                return true;
            case ConsoleKey.F3:
                FindNext(!key.Shift);
                return true;
            case ConsoleKey.F when key.Control && !key.Alt:
                BeginFind();
                return true;
            case ConsoleKey.Tab when !key.Alt:
                if (!tab.MoveFocus(!key.Shift, ViewHeight))
                {
                    _model.Say(tab, "There are no links on this page.");
                }

                return true;
            case ConsoleKey.Enter when !key.Alt:
                Activate(key.Control);
                return true;
            case ConsoleKey.Spacebar when !key.Control && !key.Alt:
                if (FocusedField() is { Kind: WebFieldKind.Checkbox or WebFieldKind.Radio or WebFieldKind.Submit or WebFieldKind.Reset or WebFieldKind.Button } field)
                {
                    ActivateField(field);
                }
                else
                {
                    tab.ScrollBy(key.Shift ? -(ViewHeight - 1) : ViewHeight - 1, ViewHeight);
                }

                return true;
            case ConsoleKey.LeftArrow or ConsoleKey.RightArrow when !key.Control && !key.Alt:
                if (FocusedField() is { Kind: WebFieldKind.Select } select)
                {
                    _model.CycleOption(tab, select, key.ConsoleKey == ConsoleKey.RightArrow ? 1 : -1);
                    return true;
                }

                return false;
            case ConsoleKey.UpArrow when !key.Alt:
                tab.ScrollBy(-1, ViewHeight);
                return true;
            case ConsoleKey.DownArrow when !key.Alt:
                tab.ScrollBy(1, ViewHeight);
                return true;
            case ConsoleKey.PageUp:
                tab.ScrollBy(-(ViewHeight - 1), ViewHeight);
                return true;
            case ConsoleKey.PageDown:
                tab.ScrollBy(ViewHeight - 1, ViewHeight);
                return true;
            case ConsoleKey.Home:
                tab.ScrollTo(0, ViewHeight);
                return true;
            case ConsoleKey.End:
                tab.ScrollTo(int.MaxValue / 2, ViewHeight);
                return true;
        }

        if (!key.Control && !key.Alt)
        {
            switch (key.KeyChar)
            {
                case '/':
                    BeginFind();
                    return true;
                case 'n':
                    FindNext(true);
                    return true;
                case 'N':
                    FindNext(false);
                    return true;
            }
        }

        return false;
    }

    private bool HandleAddressKey(KeyEvent key)
    {
        if (PasteHelper.IsPasteKey(key))
        {
            PasteHelper.Paste(text =>
            {
                string line = PasteHelper.SingleLine(text);
                if (_addressReplace)
                {
                    _address = string.Empty;
                    _addressCursor = 0;
                    _addressReplace = false;
                }

                _address = _address.Insert(_addressCursor, line);
                _addressCursor += line.Length;
            }, StateHasChanged);
            return true;
        }

        switch (key.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _mode = Mode.Browse;
                return true;
            case ConsoleKey.Enter:
                _mode = Mode.Browse;
                var typed = _address.Trim();
                if (typed.Length > 0)
                {
                    _ = _model.NavigateAsync(_model.Active, typed);
                }

                return true;
            case ConsoleKey.Backspace:
                if (_addressReplace)
                {
                    _address = string.Empty;
                    _addressCursor = 0;
                }
                else if (_addressCursor > 0)
                {
                    _address = _address.Remove(_addressCursor - 1, 1);
                    _addressCursor--;
                }

                _addressReplace = false;
                return true;
            case ConsoleKey.Delete:
                if (_addressReplace)
                {
                    _address = string.Empty;
                    _addressCursor = 0;
                }
                else if (_addressCursor < _address.Length)
                {
                    _address = _address.Remove(_addressCursor, 1);
                }

                _addressReplace = false;
                return true;
            case ConsoleKey.LeftArrow:
                _addressCursor = Math.Max(0, _addressCursor - 1);
                _addressReplace = false;
                return true;
            case ConsoleKey.RightArrow:
                _addressCursor = Math.Min(_address.Length, _addressCursor + 1);
                _addressReplace = false;
                return true;
            case ConsoleKey.Home:
                _addressCursor = 0;
                _addressReplace = false;
                return true;
            case ConsoleKey.End:
                _addressCursor = _address.Length;
                _addressReplace = false;
                return true;
            case ConsoleKey.U when key.Control:
                _address = string.Empty;
                _addressCursor = 0;
                _addressReplace = false;
                return true;
        }

        if (ConsoleKeyHelper.IsTypingKey(key))
        {
            if (_addressReplace)
            {
                _address = string.Empty;
                _addressCursor = 0;
                _addressReplace = false;
            }

            _address = _address.Insert(_addressCursor, key.KeyChar.ToString());
            _addressCursor++;
            return true;
        }

        return !(key.Control || key.Alt);
    }

    private bool HandleFindKey(KeyEvent key)
    {
        if (PasteHelper.IsPasteKey(key))
        {
            PasteHelper.Paste(text => _findText += PasteHelper.SingleLine(text), StateHasChanged);
            return true;
        }

        switch (key.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _mode = Mode.Browse;
                _findText = _findQuery;
                return true;
            case ConsoleKey.Enter:
                _mode = Mode.Browse;
                _findQuery = _findText;
                FindNext(true, inclusive: true);
                return true;
            case ConsoleKey.Backspace:
                if (_findText.Length > 0)
                {
                    _findText = _findText[..^1];
                }

                return true;
            case ConsoleKey.U when key.Control:
                _findText = string.Empty;
                return true;
        }

        if (ConsoleKeyHelper.IsTypingKey(key))
        {
            _findText += key.KeyChar;
            return true;
        }

        return !(key.Control || key.Alt);
    }

    private bool HandleFieldKey(KeyEvent key)
    {
        var tab = _model.Active;
        if (_editing is not { } field)
        {
            _mode = Mode.Browse;
            return false;
        }

        if (PasteHelper.IsPasteKey(key))
        {
            PasteHelper.Paste(
                text => _model.SetValue(tab, field, field.Value + (field.Kind == WebFieldKind.TextArea ? text.Replace("\r\n", "\n").Replace('\r', '\n') : PasteHelper.SingleLine(text))),
                StateHasChanged);
            return true;
        }

        switch (key.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _mode = Mode.Browse;
                _editing = null;
                return true;
            case ConsoleKey.Tab:
                _mode = Mode.Browse;
                _editing = null;
                tab.MoveFocus(!key.Shift, ViewHeight);
                return true;
            case ConsoleKey.Enter:
                if (field.Kind == WebFieldKind.TextArea)
                {
                    _model.SetValue(tab, field, field.Value + "\n");
                    return true;
                }

                _mode = Mode.Browse;
                _editing = null;
                _ = _model.SubmitAsync(tab, field);
                return true;
            case ConsoleKey.Backspace:
                if (field.Value.Length > 0)
                {
                    _model.SetValue(tab, field, field.Value[..^1]);
                }

                return true;
            case ConsoleKey.U when key.Control:
                _model.SetValue(tab, field, string.Empty);
                return true;
        }

        if (ConsoleKeyHelper.IsTypingKey(key))
        {
            _model.SetValue(tab, field, field.Value + key.KeyChar);
            return true;
        }

        return !(key.Control || key.Alt);
    }

    /// <summary>True while "Remove this bookmark? (y/n)" is up in the bookmark list: the next key answers it.</summary>
    private bool _confirmingRemoveBookmark;

    internal bool ConfirmingRemoveBookmark => _confirmingRemoveBookmark;

    private bool HandleListKey(KeyEvent key)
    {
        var rows = Math.Max(1, ViewHeight - 2);
        if (_confirmingRemoveBookmark)
        {
            // Esc is the list's own and leaves it as it closes the question; any other key but y withdraws it.
            _confirmingRemoveBookmark = false;
            if (key.ConsoleKey == ConsoleKey.Y && !key.Control && !key.Alt)
            {
                RemoveListEntry();
                return true;
            }

            if (key.ConsoleKey != ConsoleKey.Escape)
            {
                return true;
            }
        }

        switch (key.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _mode = Mode.Browse;
                return true;
            case ConsoleKey.UpArrow:
                _listIndex = Math.Max(0, _listIndex - 1);
                break;
            case ConsoleKey.DownArrow:
                _listIndex = Math.Min(Math.Max(0, _list.Count - 1), _listIndex + 1);
                break;
            case ConsoleKey.PageUp:
                _listIndex = Math.Max(0, _listIndex - rows);
                break;
            case ConsoleKey.PageDown:
                _listIndex = Math.Min(Math.Max(0, _list.Count - 1), _listIndex + rows);
                break;
            case ConsoleKey.Home:
                _listIndex = 0;
                break;
            case ConsoleKey.End:
                _listIndex = Math.Max(0, _list.Count - 1);
                break;
            case ConsoleKey.Enter:
                OpenListEntry(newTab: key.Control);
                return true;
            case ConsoleKey.Delete:
                if (_mode == Mode.Bookmarks && _listIndex >= 0 && _listIndex < _list.Count)
                {
                    // A bookmark is something the person made; a visit is only remembered.
                    _confirmingRemoveBookmark = true;
                    return true;
                }

                RemoveListEntry();
                return true;
            default:
                return true;
        }

        KeepListIndexVisible(rows);
        return true;
    }

    private bool HandleHelpKey(KeyEvent key)
    {
        switch (key.ConsoleKey)
        {
            case ConsoleKey.UpArrow:
                _helpScroll = Math.Max(0, _helpScroll - 1);
                return true;
            case ConsoleKey.DownArrow:
                _helpScroll = Math.Min(Math.Max(0, HelpLines.Length - ViewHeight), _helpScroll + 1);
                return true;
            default:
                _mode = Mode.Browse;
                return true;
        }
    }

    #endregion

    #region Actions

    private void BeginAddress()
    {
        _address = _model.Active.Url;
        if (_address == WebAddress.Blank)
        {
            _address = string.Empty;
        }

        _addressCursor = _address.Length;
        _addressReplace = true;
        _mode = Mode.Address;
    }

    private void BeginFind()
    {
        _findText = _findQuery;
        _mode = Mode.Find;
    }

    private void NewTab()
    {
        var tab = _model.NewTab();
        _ = _model.NavigateAsync(tab, _model.Store.Home);
        BeginAddress();
    }

    private void ToggleBookmark()
    {
        var tab = _model.Active;
        var url = tab.Url;
        if (url.Length == 0 || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            _model.Say(tab, "This page cannot be bookmarked.");
            return;
        }

        _model.Say(tab, _model.Store.ToggleBookmark(url, tab.Title) ? "Bookmarked." : "Bookmark removed.");
    }

    private void ToggleSource()
    {
        var tab = _model.Active;
        lock (tab.Sync)
        {
            if (tab.Current is not { } entry || (entry.Source is null && entry.PlainText is null))
            {
                return;
            }

            tab.ShowSource = !tab.ShowSource;
            tab.Touch();
        }
    }

    private void Save()
    {
        var tab = _model.Active;
        try
        {
            _model.Say(tab, "Saved to " + _model.Save(tab));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _model.Say(tab, "Could not save: " + exception.Message);
        }
    }

    private void OpenList(Mode mode)
    {
        _list = [.. mode == Mode.Bookmarks ? _model.Store.Bookmarks : _model.Store.History];
        _listIndex = 0;
        _listScroll = 0;
        _mode = mode;
    }

    private void OpenListEntry(bool newTab)
    {
        if (_listIndex < 0 || _listIndex >= _list.Count)
        {
            return;
        }

        var target = _list[_listIndex].Url;
        _mode = Mode.Browse;
        var tab = newTab ? _model.NewTab() : _model.Active;
        _ = _model.OpenAsync(tab, new WebFetchRequest(target));
    }

    private void RemoveListEntry()
    {
        if (_listIndex < 0 || _listIndex >= _list.Count)
        {
            return;
        }

        if (_mode == Mode.Bookmarks)
        {
            _model.Store.RemoveBookmark(_list[_listIndex].Url);
        }
        else
        {
            _model.Store.RemoveVisit(_list[_listIndex].Url);
        }

        _list.RemoveAt(_listIndex);
        _listIndex = Math.Min(_listIndex, Math.Max(0, _list.Count - 1));
        KeepListIndexVisible(Math.Max(1, ViewHeight - 2));
    }

    private void KeepListIndexVisible(int rows)
    {
        if (_listIndex < _listScroll)
        {
            _listScroll = _listIndex;
        }
        else if (_listIndex >= _listScroll + rows)
        {
            _listScroll = _listIndex - rows + 1;
        }
    }

    private WebField? FocusedField()
    {
        var tab = _model.Active;
        return tab.FocusedWebItem is { IsField: true } item ? _model.FieldOf(tab, item) : null;
    }

    /// <summary>Enter on the focused item. With nothing focused the first link or control on screen is taken, as Tab would.</summary>
    private void Activate(bool newTab)
    {
        var tab = _model.Active;
        if (tab.FocusedWebItem is not { } item)
        {
            return;
        }

        if (item.IsField)
        {
            if (_model.FieldOf(tab, item) is { } field)
            {
                ActivateField(field);
            }

            return;
        }

        _ = _model.OpenLinkAsync(tab, item.Id, newTab);
    }

    private void ActivateField(WebField field)
    {
        var tab = _model.Active;
        switch (field.Kind)
        {
            case WebFieldKind.Text or WebFieldKind.Password or WebFieldKind.TextArea:
                if (field.ReadOnly)
                {
                    return;
                }

                _editing = field;
                _mode = Mode.Field;
                break;
            case WebFieldKind.Checkbox or WebFieldKind.Radio:
                _model.Toggle(tab, field);
                break;
            case WebFieldKind.Select:
                _model.CycleOption(tab, field, 1);
                break;
            case WebFieldKind.Submit:
                _ = _model.SubmitAsync(tab, field);
                break;
            case WebFieldKind.Reset:
                _model.ResetForm(tab, field);
                break;
            case WebFieldKind.Button:
                _model.Say(tab, "This button needs JavaScript, which the Web Browser does not run.");
                break;
            case WebFieldKind.File:
                _model.Say(tab, "Uploading files is not supported.");
                break;
        }
    }

    private void FindNext(bool forward, bool inclusive = false)
    {
        var tab = _model.Active;
        if (_findQuery.Length == 0)
        {
            return;
        }

        lock (tab.Sync)
        {
            if (tab.Current is not { Page: { } page } entry)
            {
                return;
            }

            // A new search starts on the line at the top of the screen; "next" and "previous" start beside it.
            var count = page.Lines.Count;
            var start = entry.Scroll + (inclusive ? 0 : forward ? 1 : -1);
            for (var i = 0; i < count; i++)
            {
                var line = ((forward ? start + i : start - i) % count + count) % count;
                if (page.Lines[line].Text.Contains(_findQuery, StringComparison.OrdinalIgnoreCase))
                {
                    entry.Scroll = Math.Clamp(line, 0, Math.Max(0, count - ViewHeight));
                    tab.Message = string.Empty;
                    tab.Touch();
                    return;
                }
            }

            tab.Message = "“" + _findQuery + "” was not found.";
            tab.Touch();
        }
    }

    #endregion

    #region Mouse

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.WheelDelta != 0)
        {
            ScrollWheel(-mouseEvent.WheelDelta * 3);
            StateHasChanged();
            return true;
        }

        if (!mouseEvent.LeftClicked || mouseEvent.IsRelease)
        {
            return await base.HandleMouseEventAsync(mouseEvent);
        }

        var x = (int)mouseEvent.Position.X;
        var y = (int)mouseEvent.Position.Y;
        var relativeY = y - _screenY;
        var tab = _model.Active;

        if (relativeY == 0)
        {
            foreach (var (x0, x1, index) in _tabHits)
            {
                if (x >= x0 && x < x1)
                {
                    _model.SelectTab(index);
                    _mode = Mode.Browse;
                    StateHasChanged();
                    return true;
                }
            }

            if (x >= _newTab.X0 && x < _newTab.X1)
            {
                NewTab();
                StateHasChanged();
                return true;
            }

            return true;
        }

        if (relativeY == 1)
        {
            if (x >= _back.X0 && x < _back.X1)
            {
                _model.GoBack(tab);
            }
            else if (x >= _forward.X0 && x < _forward.X1)
            {
                _model.GoForward(tab);
            }
            else if (x >= _reload.X0 && x < _reload.X1)
            {
                if (tab.IsLoading)
                {
                    _model.Stop(tab);
                }
                else
                {
                    _ = _model.ReloadAsync(tab);
                }
            }
            else if (x >= _star.X0 && x < _star.X1)
            {
                ToggleBookmark();
            }
            else if (x >= _addressBox.X0 && x < _addressBox.X1)
            {
                BeginAddress();
            }

            StateHasChanged();
            return true;
        }

        if (relativeY >= ChromeRows - 1 && relativeY < ChromeRows - 1 + _contentHeight)
        {
            ClickContent(x - _screenX, relativeY - (ChromeRows - 1));
            StateHasChanged();
            return true;
        }

        return true;
    }

    private void ScrollWheel(int lines)
    {
        switch (_mode)
        {
            case Mode.Bookmarks or Mode.History:
                _listIndex = Math.Clamp(_listIndex + lines, 0, Math.Max(0, _list.Count - 1));
                KeepListIndexVisible(Math.Max(1, ViewHeight - 2));
                break;
            case Mode.Help:
                _helpScroll = Math.Clamp(_helpScroll + lines, 0, Math.Max(0, HelpLines.Length - ViewHeight));
                break;
            default:
                _model.Active.ScrollBy(lines, ViewHeight);
                break;
        }
    }

    private void ClickContent(int column, int row)
    {
        if (_mode is Mode.Bookmarks or Mode.History)
        {
            var index = _listScroll + row - 1;
            if (index >= 0 && index < _list.Count)
            {
                _listIndex = index;
                OpenListEntry(newTab: false);
            }

            return;
        }

        if (_mode == Mode.Help)
        {
            _mode = Mode.Browse;
            return;
        }

        if (_mode is Mode.Address or Mode.Find)
        {
            _mode = Mode.Browse;
        }

        var tab = _model.Active;
        WebItem? hit = null;
        lock (tab.Sync)
        {
            if (tab.Current is not { Page: { } page } entry)
            {
                return;
            }

            var lineIndex = entry.Scroll + row;
            if (lineIndex < 0 || lineIndex >= page.Lines.Count)
            {
                return;
            }

            var at = 0;
            foreach (var span in page.Lines[lineIndex].Spans)
            {
                var end = at + span.Text.Length;
                if (column >= at && column < end)
                {
                    var itemIndex = FindItem(page, span);
                    if (itemIndex >= 0)
                    {
                        entry.FocusedItem = itemIndex;
                        hit = page.Items[itemIndex];
                        tab.Touch();
                    }

                    break;
                }

                at = end;
            }
        }

        if (hit is { } item)
        {
            _mode = Mode.Browse;
            if (item.IsField)
            {
                if (_model.FieldOf(tab, item) is { } field)
                {
                    ActivateField(field);
                }
            }
            else
            {
                _ = _model.OpenLinkAsync(tab, item.Id);
            }
        }
    }

    private static int FindItem(WebPage page, WebSpan span)
    {
        for (var i = 0; i < page.Items.Count; i++)
        {
            var item = page.Items[i];
            if (span.Field >= 0 ? item.IsField && item.Id == span.Field : span.Link >= 0 && !item.IsField && item.Id == span.Link)
            {
                return i;
            }
        }

        return -1;
    }

    #endregion

    #region Draw

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        var width = (int)ViewSize.Width - 2;
        var height = (int)ViewSize.Height - 2;
        if (width < 20 || height < ChromeRows + 2)
        {
            return;
        }

        var scheme = ThemeService.CurrentColorScheme;
        _screenX = (int)AbsolutePosition.X + 1;
        _screenY = (int)AbsolutePosition.Y + 1;
        _contentWidth = width - 1;
        _contentHeight = height - ChromeRows;

        var tab = _model.Active;
        DrawTabs(buffer, scheme, _screenX, _screenY, width);
        DrawToolbar(buffer, scheme, tab, _screenX, _screenY + 1, width);

        switch (_mode)
        {
            case Mode.Bookmarks or Mode.History:
                DrawList(buffer, scheme, _screenX, _screenY + 2, width, _contentHeight);
                break;
            case Mode.Help:
                DrawHelp(buffer, scheme, _screenX, _screenY + 2, width, _contentHeight);
                break;
            default:
                _model.EnsureLayout(tab, _contentWidth);
                DrawPage(buffer, scheme, tab, _screenX, _screenY + 2, width, _contentHeight);
                break;
        }

        DrawStatus(buffer, scheme, tab, _screenX, _screenY + height - 1, width);
    }

    private void DrawTabs(IBuffer buffer, ColorScheme scheme, int x, int y, int width)
    {
        _tabHits.Clear();
        var cells = new ConxelString();
        var tabs = _model.Tabs;
        var active = _model.ActiveIndex;
        var maxEach = Math.Clamp((width - 6) / Math.Max(1, tabs.Count), 6, MaxTabWidth);
        var at = 0;
        for (var i = 0; i < tabs.Count && at < width - 4; i++)
        {
            var tab = tabs[i];
            var label = " " + (i + 1) + " " + Shorten(tab.IsLoading ? "…" + tab.Title : tab.Title, maxEach - 4) + " ";
            label = label.PadRight(Math.Min(maxEach, width - at));
            if (label.Length > width - at)
            {
                label = label[..(width - at)];
            }

            var isActive = i == active;
            cells.Append(label, isActive ? scheme.SelectedFocusedForeground : scheme.Foreground, isActive ? scheme.SelectedFocusedBackground : scheme.ButtonBackground, isBold: isActive);
            _tabHits.Add((x + at, x + at + label.Length, i));
            at += label.Length;
            if (at < width)
            {
                cells.Append(' ', scheme.Foreground, scheme.Background);
                at++;
            }
        }

        _newTab = (x + at, x + at + 3);
        if (at + 3 <= width)
        {
            cells.Append(" + ", Color.Green, scheme.Background, isBold: true);
            at += 3;
        }

        WritePadded(buffer, x, y, width, cells, scheme);
    }

    private void DrawToolbar(IBuffer buffer, ColorScheme scheme, WebTab tab, int x, int y, int width)
    {
        var cells = new ConxelString();
        void Button(string glyph, bool enabled, out (int, int) hit)
        {
            hit = (x + cells.Count, x + cells.Count + glyph.Length + 2);
            cells.Append(" " + glyph + " ", enabled ? scheme.Foreground : scheme.DisabledForeground, scheme.Background, isBold: enabled);
        }

        Button("◀", tab.CanGoBack, out _back);
        Button("▶", tab.CanGoForward, out _forward);
        Button(tab.IsLoading ? "✕" : "⟳", true, out _reload);
        var bookmarked = tab.Url.Length > 0 && _model.Store.IsBookmarked(tab.Url);
        _star = (x + cells.Count, x + cells.Count + 3);
        cells.Append(bookmarked ? " ★ " : " ☆ ", bookmarked ? Color.Yellow : scheme.Foreground, scheme.Background);

        var boxWidth = Math.Max(4, width - cells.Count - 1);
        _addressBox = (x + cells.Count, x + cells.Count + boxWidth);
        var editing = _mode == Mode.Address;
        var inner = boxWidth - 2;
        string text;
        if (editing)
        {
            var shown = _address.Insert(_addressCursor, "█");
            var start = Math.Max(0, Math.Min(_addressCursor + 1 - inner + 1, Math.Max(0, shown.Length - inner)));
            text = shown.Length > inner ? shown.Substring(start, inner) : shown;
        }
        else
        {
            var url = tab.Url;
            text = url.Length == 0 ? "Press Ctrl+L to enter an address" : Shorten(url, inner);
        }

        var background = editing ? Color.White : scheme.ButtonBackground;
        var foreground = editing ? Color.Black : tab.Url.Length == 0 ? scheme.DisabledForeground : scheme.Foreground;
        cells.Append("[", scheme.Foreground, scheme.Background);
        cells.Append(text.PadRight(inner), foreground, background);
        cells.Append("]", scheme.Foreground, scheme.Background);
        WritePadded(buffer, x, y, width, cells, scheme);
    }

    private void DrawPage(IBuffer buffer, ColorScheme scheme, WebTab tab, int x, int y, int width, int height)
    {
        WebPage? page;
        WebDocument? document;
        int scroll;
        int focusedIndex;
        string notice;
        lock (tab.Sync)
        {
            var entry = tab.Current;
            page = entry?.Page;
            document = entry?.Document;
            scroll = entry?.Scroll ?? 0;
            focusedIndex = entry?.FocusedItem ?? -1;
            notice = tab.IsLoading && entry is null ? "Loading " + tab.LoadingUrl + "…" : entry is null ? "Press Ctrl+L to enter an address, or F1 for help." : "Laying out the page…";
        }

        WebItem? focus = page is not null && focusedIndex >= 0 && focusedIndex < page.Items.Count ? page.Items[focusedIndex] : null;
        var contentWidth = width - 1;
        var blank = new Conxel(' ', scheme.Foreground, scheme.Background);

        for (var row = 0; row < height; row++)
        {
            var cells = new Conxel[width];
            Array.Fill(cells, blank);

            if (page is null)
            {
                if (row == height / 2)
                {
                    var message = notice.Length > contentWidth ? notice[..contentWidth] : notice;
                    var start = Math.Max(0, (contentWidth - message.Length) / 2);
                    for (var i = 0; i < message.Length; i++)
                    {
                        cells[start + i] = new Conxel(message[i], scheme.DisabledForeground, scheme.Background);
                    }
                }
            }
            else if (scroll + row < page.Lines.Count)
            {
                ComposeLine(cells, page.Lines[scroll + row], document, focus, contentWidth, scheme);
                if (_findQuery.Length > 0)
                {
                    Highlight(cells, contentWidth, _findQuery);
                }
            }

            if (page is not null)
            {
                cells[width - 1] = ScrollbarCell(row, height, scroll, page.Lines.Count, scheme);
            }

            buffer.Write(ZOrder, new Position(x, y + row), new Size(width, 1), new ConxelString(cells));
        }
    }

    private static Conxel ScrollbarCell(int row, int height, int scroll, int total, ColorScheme scheme)
    {
        if (total <= height || height <= 0)
        {
            return new Conxel(' ', scheme.Foreground, scheme.Background);
        }

        var thumbSize = Math.Max(1, height * height / total);
        var thumbStart = (int)((long)(height - thumbSize) * scroll / Math.Max(1, total - height));
        var inThumb = row >= thumbStart && row < thumbStart + thumbSize;
        return new Conxel(inThumb ? '█' : '░', inThumb ? scheme.Foreground : scheme.DisabledForeground, scheme.Background);
    }

    private void ComposeLine(Conxel[] cells, WebLine line, WebDocument? document, WebItem? focus, int width, ColorScheme scheme)
    {
        var column = 0;
        foreach (var span in line.Spans)
        {
            if (column >= width)
            {
                break;
            }

            var isFocused = focus is { } f && (span.Field >= 0 ? f.IsField && f.Id == span.Field : span.Link >= 0 && !f.IsField && f.Id == span.Link);
            string text = span.Text;
            Color foreground = scheme.Foreground;
            Color background = scheme.Background;
            bool bold = span.Style.HasFlag(WebStyle.Bold);
            bool italic = span.Style.HasFlag(WebStyle.Italic);
            bool underline = span.Style.HasFlag(WebStyle.Underline);
            bool strike = span.Style.HasFlag(WebStyle.Strike);

            if (span.Field >= 0 && document is not null && span.Field < document.Fields.Count)
            {
                var field = document.Fields[span.Field];
                var editing = _mode == Mode.Field && ReferenceEquals(_editing, field);
                text = field.Render(span.Text.Length, editing);
                foreground = editing ? Color.Black : field.IsButton ? Color.White : scheme.Foreground;
                background = editing ? Color.White : field.IsButton ? Color.DarkBlue : scheme.ButtonBackground;
                if (field.Disabled)
                {
                    foreground = scheme.DisabledForeground;
                }
            }
            else
            {
                if (span.Style.HasFlag(WebStyle.Heading))
                {
                    foreground = Color.Yellow;
                    bold = true;
                }

                if (span.Style.HasFlag(WebStyle.Code))
                {
                    foreground = Color.Green;
                }

                if (span.Style.HasFlag(WebStyle.Quote))
                {
                    foreground = Color.Gray;
                }

                if (span.Style.HasFlag(WebStyle.Dim))
                {
                    foreground = Color.DarkGray;
                }

                if (span.Style.HasFlag(WebStyle.Link))
                {
                    foreground = Color.Cyan;
                    underline = true;
                }

                if (span.Style.HasFlag(WebStyle.Mark))
                {
                    foreground = Color.Black;
                    background = Color.Yellow;
                }
            }

            if (isFocused)
            {
                foreground = scheme.SelectedFocusedForeground;
                background = scheme.SelectedFocusedBackground;
            }

            for (var i = 0; i < text.Length && column < width; i++, column++)
            {
                cells[column] = new Conxel(text[i], foreground, background, bold, italic, underline, strike);
            }
        }
    }

    private static void Highlight(Conxel[] cells, int width, string query)
    {
        var text = new string(cells.Take(width).Select(c => c.Char).ToArray());
        var at = 0;
        while (at < text.Length && text.IndexOf(query, at, StringComparison.OrdinalIgnoreCase) is var found and >= 0)
        {
            for (var i = found; i < Math.Min(width, found + query.Length); i++)
            {
                cells[i].ForegroundColor = Color.Black;
                cells[i].BackgroundColor = Color.Yellow;
            }

            at = found + Math.Max(1, query.Length);
        }
    }

    private void DrawList(IBuffer buffer, ColorScheme scheme, int x, int y, int width, int height)
    {
        var heading = _mode == Mode.Bookmarks ? "Bookmarks" : "History";
        var hint = _mode == Mode.Bookmarks
            ? "Enter:open   Ctrl+Enter:new tab   Del:remove   Esc:back"
            : "Enter:open   Ctrl+Enter:new tab   Del:forget   Esc:back";
        if (_confirmingRemoveBookmark && _listIndex >= 0 && _listIndex < _list.Count)
        {
            var entry = _list[_listIndex];
            WriteText(buffer, x, y, width, $" Remove the bookmark \"{(entry.Title.Length > 0 ? entry.Title : entry.Url)}\"? (y/n)", Color.Red, scheme.Background, true);
        }
        else
        {
            WriteText(buffer, x, y, width, $" {heading} ({_list.Count})   {hint}", Color.Yellow, scheme.Background, true);
        }

        var rows = Math.Max(0, height - 1);
        for (var row = 0; row < rows; row++)
        {
            var index = _listScroll + row;
            string text;
            Color foreground;
            Color background;
            if (index < _list.Count)
            {
                var entry = _list[index];
                var selected = index == _listIndex;
                var title = entry.Title == entry.Url ? string.Empty : entry.Title;
                var space = Math.Max(10, width - 4);
                text = " " + (title.Length > 0 ? Shorten(title, space / 2) + "  " + Shorten(entry.Url, space - Math.Min(title.Length, space / 2) - 2) : Shorten(entry.Url, space));
                foreground = selected ? scheme.SelectedFocusedForeground : scheme.Foreground;
                background = selected ? scheme.SelectedFocusedBackground : scheme.Background;
            }
            else
            {
                text = row == 0 && _list.Count == 0 ? (_mode == Mode.Bookmarks ? " No bookmarks yet. Press Ctrl+D on a page to add one." : " Nothing visited yet.") : string.Empty;
                foreground = scheme.DisabledForeground;
                background = scheme.Background;
            }

            WriteText(buffer, x, y + 1 + row, width, text, foreground, background, false);
        }
    }

    private void DrawHelp(IBuffer buffer, ColorScheme scheme, int x, int y, int width, int height)
    {
        for (var row = 0; row < height; row++)
        {
            var index = _helpScroll + row;
            var text = index < HelpLines.Length ? HelpLines[index] : string.Empty;
            var heading = index < HelpLines.Length && text.Length > 0 && !text.StartsWith(' ') && index != HelpLines.Length - 1;
            WriteText(buffer, x, y + row, width, " " + text, heading ? Color.Yellow : scheme.Foreground, scheme.Background, heading);
        }
    }

    private void DrawStatus(IBuffer buffer, ColorScheme scheme, WebTab tab, int x, int y, int width)
    {
        string left;
        var color = scheme.Foreground;
        switch (_mode)
        {
            case Mode.Find:
                left = " Find: " + _findText + "█   Enter:search   Esc:cancel   Ctrl+V:paste";
                break;
            case Mode.Field:
                left = " Editing — type, Enter:send form, Tab:next, Esc:done";
                break;
            case Mode.Address:
                left = " Enter:go   Esc:cancel   Ctrl+U:clear   Ctrl+V:paste";
                break;
            default:
                string message;
                string target = string.Empty;
                int total = 0;
                int scroll = 0;
                lock (tab.Sync)
                {
                    message = tab.Message;
                    if (tab.Current is { Page: { } page, Document: var document } entry)
                    {
                        total = page.Lines.Count;
                        scroll = entry.Scroll;
                        if (entry.FocusedItem >= 0 && entry.FocusedItem < page.Items.Count && document is not null)
                        {
                            var item = page.Items[entry.FocusedItem];
                            if (!item.IsField && item.Id < document.Links.Count)
                            {
                                target = document.Resolve(document.Links[item.Id].Href);
                            }
                        }
                    }
                }

                if (message.Length > 0)
                {
                    left = " " + message;
                    color = tab.IsLoading ? Color.Yellow : Color.Cyan;
                }
                else if (target.Length > 0)
                {
                    left = " " + target;
                }
                else
                {
                    // Hints that no longer fit are dropped, least useful first — the line used to be
                    // cut at the right edge, which on 80 columns took "Esc:close" with it.
                    left = " " + FitHints(
                        ["F1:help", "Ctrl+L:address", "Tab:links", "Ctrl+T:new tab", "F2:bookmarks", "Esc:close"],
                        [4, 3, 2, 1, 0, 5],
                        Math.Max(1, width - 8));
                    color = scheme.DisabledForeground;
                }

                var right = total == 0 ? string.Empty : total <= ViewHeight ? " All " : $" {Math.Min(100, (scroll + ViewHeight) * 100 / total)}% ";
                left = Shorten(left, Math.Max(1, width - right.Length)).PadRight(Math.Max(0, width - right.Length)) + right;
                break;
        }

        Status = left.Trim();
        WriteText(buffer, x, y, width, left, color, scheme.Background, false);
    }

    private void WritePadded(IBuffer buffer, int x, int y, int width, ConxelString cells, ColorScheme scheme)
    {
        cells.Expand(width, scheme.Foreground, scheme.Background);
        var list = cells.String.Count > width ? cells.String.GetRange(0, width) : cells.String;
        buffer.Write(ZOrder, new Position(x, y), new Size(width, 1), new ConxelString(list.ToArray()));
    }

    private void WriteText(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background, bool bold)
    {
        text = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(ZOrder, new Position(x, y), new Size(width, 1), new ConxelString(text, foreground, background, bold));
    }

    /// <summary>
    /// Joins the hints with two spaces, leaving out the ones named first in <paramref name="dropOrder"/>
    /// (indexes into <paramref name="hints"/>) until the line fits in <paramref name="max"/> columns.
    /// </summary>
    internal static string FitHints(IReadOnlyList<string> hints, IReadOnlyList<int> dropOrder, int max)
    {
        var kept = Enumerable.Range(0, hints.Count).ToList();
        string Join() => string.Join("  ", kept.Select(i => hints[i]));
        foreach (int index in dropOrder)
        {
            if (Join().Length <= max || kept.Count <= 1)
            {
                break;
            }
            kept.Remove(index);
        }

        return Join();
    }

    private static string Shorten(string text, int max)
    {
        if (max <= 0)
        {
            return string.Empty;
        }

        return text.Length <= max ? text : max <= 1 ? text[..max] : text[..(max - 1)] + "…";
    }

    #endregion
}
