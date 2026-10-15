using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Lists the colour schemes and applies the one under the cursor with Enter. The current one is
/// marked, and the dialog stays open after applying, so trying a theme is one key and going back is
/// another.
///
/// <para>Three things were wrong with it: it built a new list box on <em>every</em> layout pass (so
/// each resize or reopening stacked another list on top of the last), it sized that list from the
/// dialog before the dialog had been sized, and nothing in it ever applied a theme — Enter ticked a
/// row and that was all.</para>
/// </summary>
public class ThemeManagerDialog : Dialog
{
    private const int MinimumWidth = 36;

    private readonly ConcurrentObservableCollection<ColorScheme> _themes = [];
    private readonly ListBox<ColorScheme> _listBoxThemes;
    private string _status = string.Empty;

    private readonly iTerminal.Core.Services.ISettingsService? _settingsService;

    public ThemeManagerDialog(IThemeService themeService, iTerminal.Core.Services.ISettingsService? settingsService = null)
        : base(nameof(ThemeManagerDialog), themeService)
    {
        _settingsService = settingsService;
        Title = "Theme manager";

        var listModel = new ListBoxModel<ColorScheme>(_themes)
        {
            TransformItem = TransformItem
        };
        _listBoxThemes = new ListBox<ColorScheme>(listModel, themeService)
        {
            EmptyText = " No themes available.",
        };
        AddControl(_listBoxThemes);
    }

    internal ListBox<ColorScheme> ListBoxForTest => _listBoxThemes;

    internal string StatusForTest => _status;

    public override async Task OnInitializeAsync()
    {
        _themes.ReplaceAll([.. ThemeService.ColorSchemes.Values]);
        _listBoxThemes.UpdateModel();
        _status = string.Empty;

        // Start on the theme in use, so Enter straight away is a no-op rather than a surprise.
        var currentIndex = IndexOf(ThemeService.CurrentColorScheme.Theme);
        _listBoxThemes.ScrollableModel.CursorPosition.Y = Math.Max(0, currentIndex);
        _listBoxThemes.ScrollableModel.ScrollPosition.Y = 0;

        await base.OnInitializeAsync();
    }

    private int IndexOf(Theme theme)
    {
        var items = _themes.AsSnapshot();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Theme == theme)
            {
                return i;
            }
        }

        return -1;
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            var width = Math.Max(MinimumWidth, (int)(Parent.ViewSize.Width * 0.4));
            // Border, list, blank, hint row, border: the list gets exactly the rows it needs.
            var height = Math.Max(7, _themes.Count + 5);
            CenterInParent(new Size(
                Math.Min(width, (int)Parent.ViewSize.Width),
                Math.Min(height, (int)Parent.ViewSize.Height)));
        }

        // Sized from the dialog *after* the dialog has its own size; the children must also be
        // placed before the base pass lays them out and focuses the first one.
        _listBoxThemes.Position = new Position(1, 1);
        _listBoxThemes.ViewSize = new Size(
            Math.Max(0, (int)ViewSize.Width - 2),
            Math.Max(0, (int)ViewSize.Height - 4));

        base.OnInitializeLayout();
    }

    private ListItem<ColorScheme> TransformItem(ColorScheme colorScheme, CancellationToken cancellationToken)
    {
        var marker = colorScheme.Theme == ThemeService.CurrentColorScheme.Theme ? "● " : "  ";
        return new ListItem<ColorScheme>(colorScheme)
        {
            Value = marker + colorScheme.Theme,
            IsEnabled = true
        };
    }

    private ColorScheme? SelectedScheme()
    {
        var items = _listBoxThemes.ListModel.ListItems;
        var index = (int)_listBoxThemes.ScrollableModel.CursorPosition.Y;
        return index >= 0 && index < items.Count ? items[index].Item : null;
    }

    private void ApplySelected()
    {
        var scheme = SelectedScheme();
        if (scheme is null)
        {
            return;
        }

        if (!ThemeService.TrySetTheme(scheme.Theme))
        {
            _status = $"The {scheme.Theme} theme cannot be applied here.";
            StateHasChanged();
            return;
        }

        // Kept for the next start-up too: it used to last only until the app closed.
        iTerminal.Core.Services.ThemePersistence.Save(_settingsService, scheme.Theme);
        _status = $"{scheme.Theme} theme applied.";

        // The marker moves with the theme, so the rows are rebuilt (the list caches what it
        // transformed); and everything already on screen was drawn in the old colours, so the whole
        // screen has to repaint, not only this dialog.
        var cursor = _listBoxThemes.ScrollableModel.CursorPosition.Y;
        _themes.ReplaceAll([.. ThemeService.ColorSchemes.Values]);
        _listBoxThemes.UpdateModel();
        _listBoxThemes.ScrollableModel.CursorPosition.Y = cursor;
        GetRootControl()?.Refresh();
        StateHasChanged();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // Claimed ahead of the list box, whose own Enter binding only ticks the row.
        if (keyEvent.ConsoleKey is ConsoleKey.Enter or ConsoleKey.Spacebar
            && !keyEvent.Control && !keyEvent.Alt)
        {
            ApplySelected();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        var innerWidth = (int)ViewSize.Width - 2;
        if (innerWidth <= 0 || ViewSize.Height < 4)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var text = _status.Length > 0 ? _status : "Enter:apply  Esc:close";
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + ViewSize.Height - 2),
            new Size(innerWidth, 1),
            new ConxelString((" " + text).Cut(innerWidth).ExpandRight(innerWidth), colorScheme.Foreground, colorScheme.Background));
    }
}
