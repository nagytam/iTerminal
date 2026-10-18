using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.CommandPalette;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Shift+F1: every command the app can run right now, by name. Type to narrow the list, ↑/↓ to
/// choose, Enter to run, Esc to leave. F1 tells what the keys do; this runs a command without
/// knowing its key — and shows the key on the way, so the next time the palette is not needed.
///
/// <para>The list is handed over by <see cref="Commands.CommandPaletteDialogCommand"/> before the
/// dialog takes focus (see <see cref="CommandCatalog"/>). Running a command closes the palette
/// <em>first</em>: closing gives focus back to whoever had it, and a panel's commands only run
/// against the focused panel.</para>
/// </summary>
public class CommandPaletteDialog : Dialog
{
    /// <summary>The settings key holding the titles of the commands run last, newest first.</summary>
    internal const string RecentKey = "CommandPalette.Recent";

    private const int MaxRecent = 8;

    private readonly ISettingsService _settingsService;
    private readonly TextBox _query;
    private readonly PaletteList _list;
    private IReadOnlyList<PaletteEntry> _all = [];
    private string _lastQuery = string.Empty;

    public CommandPaletteDialog(
        ISettingsService settingsService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(CommandPaletteDialog), themeService)
    {
        _settingsService = settingsService;
        Title = "Commands";
        _query = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Run", IsReadOnly = false, Placeholder = "type to filter commands" };
        _list = new PaletteList(themeService);
        AddControl(_query);
        AddControl(_list);
    }

    /// <summary>The commands to offer; set before the dialog is shown.</summary>
    public void Load(IReadOnlyList<PaletteEntry> entries)
    {
        _all = entries;
        _lastQuery = string.Empty;
        _query.Text = string.Empty;
        Refilter(force: true);
    }

    public string Query
    {
        get => _query.Text ?? string.Empty;
        set
        {
            _query.Text = value ?? string.Empty;
            Refilter();
        }
    }

    /// <summary>What the list shows now, best match first.</summary>
    public IReadOnlyList<PaletteEntry> Matches => _list.Items;

    public PaletteEntry? Selected => _list.SelectedItem;

    internal IReadOnlyList<string> Recent =>
        (_settingsService.Get(RecentKey) ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        Refilter(force: true);
        _query.SetFocus();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            var width = Math.Clamp((int)Parent.ViewSize.Width - 8, 40, 100);
            var height = Math.Clamp((int)Parent.ViewSize.Height - 6, 10, 26);
            CenterInParent(new Size(width, height));
        }

        _query.Position = new Position(2, 1);
        _query.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), 1);
        _list.Position = new Position(1, 3);
        _list.ViewSize = new Size(Math.Max(1, ViewSize.Width - 2), Math.Max(1, ViewSize.Height - 6));
        base.OnInitializeLayout();
    }

    /// <summary>The list follows the query as it is typed; the text box has no event of its own for that, so the idle pass checks.</summary>
    public override void OnIdle()
    {
        base.OnIdle();
        Refilter();
    }

    private void Refilter(bool force = false)
    {
        var query = Query;
        if (!force && query == _lastQuery)
        {
            return;
        }

        _lastQuery = query;
        _list.SetItems(CommandCatalog.Filter(_all, query, Recent));
        StateHasChanged();
    }

    /// <summary>
    /// The list keys are claimed ahead of the text box, which holds focus so typing always narrows
    /// the list: ↑/↓, PgUp/PgDn choose, Enter runs. Everything else goes to the text box.
    /// </summary>
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    _list.Move(-1);
                    StateHasChanged();
                    return true;
                case ConsoleKey.DownArrow:
                    _list.Move(1);
                    StateHasChanged();
                    return true;
                case ConsoleKey.PageUp:
                    _list.Move(-_list.PageSize);
                    StateHasChanged();
                    return true;
                case ConsoleKey.PageDown:
                    _list.Move(_list.PageSize);
                    StateHasChanged();
                    return true;
                case ConsoleKey.Enter:
                    await RunSelectedAsync();
                    return true;
            }
        }

        var handled = await base.HandleKeyEventAsync(keyEvent);
        Refilter();
        return handled;
    }

    /// <summary>Closes the palette, then runs the chosen command where focus has gone back to.</summary>
    public async Task<bool> RunSelectedAsync()
    {
        Refilter();
        if (Selected is not { } entry)
        {
            return false;
        }

        Remember(entry.Title);
        Close();
        return await entry.Command.RunAsync(null);
    }

    private void Remember(string title)
    {
        var recent = new List<string> { title };
        recent.AddRange(Recent.Where(t => !string.Equals(t, title, StringComparison.OrdinalIgnoreCase)));
        _settingsService.Set(RecentKey, string.Join('\n', recent.Take(MaxRecent)));
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;

        // The rule under the query, and the chosen command's description above the bottom edge.
        var rule = new ConxelString(Border.Horizontal.Repeat(Math.Max(0, width - 2)), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 2), new Size(rule.Count, 1), rule);

        var description = Selected?.Description ?? string.Empty;
        var text = (" " + description).Shrink(Math.Max(0, width - 2))?.ExpandRight(Math.Max(0, width - 2)) ?? string.Empty;
        var line = new ConxelString(text, colorScheme.DisabledForeground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + ViewSize.Height - 2), new Size(line.Count, 1), line);

        var footer = $" {Matches.Count} of {_all.Count}  ↑↓:choose  Enter:run  Esc:close ".FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }

    /// <summary>The rows: a command's title on the left, its key on the right, the chosen one highlighted.</summary>
    private sealed class PaletteList(IThemeService themeService) : Control(themeService)
    {
        private int _top;

        public IReadOnlyList<PaletteEntry> Items { get; private set; } = [];

        public int SelectedIndex { get; private set; }

        public PaletteEntry? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

        public int PageSize => Math.Max(1, (int)ViewSize.Height - 1);

        public void SetItems(IReadOnlyList<PaletteEntry> items)
        {
            Items = items;
            SelectedIndex = items.Count == 0 ? -1 : 0;
            _top = 0;
            StateHasChanged();
        }

        public void Move(int step)
        {
            if (Items.Count == 0)
            {
                return;
            }

            SelectedIndex = Math.Clamp(SelectedIndex + step, 0, Items.Count - 1);
            var height = Math.Max(1, (int)ViewSize.Height);
            if (SelectedIndex < _top)
            {
                _top = SelectedIndex;
            }
            else if (SelectedIndex >= _top + height)
            {
                _top = SelectedIndex - height + 1;
            }

            StateHasChanged();
        }

        public override void Draw(IBuffer buffer)
        {
            var colorScheme = ThemeService.CurrentColorScheme;
            var width = (int)ViewSize.Width;
            var height = (int)ViewSize.Height;
            for (var row = 0; row < height; row++)
            {
                var index = _top + row;
                string text;
                Color foreground = colorScheme.Foreground, background = colorScheme.Background;
                if (index < Items.Count)
                {
                    var entry = Items[index];
                    var key = entry.Key.Length > 0 ? entry.Key + " " : string.Empty;
                    var titleWidth = Math.Max(0, width - 1 - key.Length);
                    text = " " + (entry.Title.Shrink(titleWidth - 1) ?? string.Empty).ExpandRight(titleWidth) + key;
                    if (index == SelectedIndex)
                    {
                        foreground = colorScheme.SelectedFocusedForeground;
                        background = colorScheme.SelectedFocusedBackground;
                    }
                }
                else
                {
                    text = index == 0 && Items.Count == 0 ? " No command matches." : string.Empty;
                }

                // Every cell of the rectangle, every time: a shorter list must not leave old rows behind.
                var line = new ConxelString(text.ExpandRight(width)[..width], foreground, background);
                buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(width, 1), line);
            }
        }
    }
}
