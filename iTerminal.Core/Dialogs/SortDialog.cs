using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Shift+F11: the order of the panel it was opened over — by name, extension, size or date, or as
/// the drive lists it — and whether reversed. ↑/↓ and Enter choose; the first letter of a mode
/// chooses it at once; <c>r</c> reverses and leaves the dialog open, since the direction is usually
/// wanted together with a mode.
///
/// <para>The choice is the panel's: it is saved under the panel's name and survives a restart and
/// a change of drive (<see cref="PanelSort.SettingsKey"/>).</para>
/// </summary>
public class SortDialog : Dialog
{
    public static readonly IReadOnlyList<PanelSortMode> Modes =
        [PanelSortMode.Name, PanelSortMode.Extension, PanelSortMode.Size, PanelSortMode.Date, PanelSortMode.AsListed];

    private readonly ISettingsService _settingsService;
    private readonly InfoLines _lines;
    private IContentModel? _target;
    private int _cursor;

    public SortDialog(ISettingsService settingsService, IThemeService themeService)
        : base(nameof(SortDialog), themeService)
    {
        _settingsService = settingsService;
        Title = iConsole.Localization.UiText.Get("Dialog.SortBy", "Sort by");
        _lines = new InfoLines(themeService);
        AddControl(_lines);
    }

    /// <summary>The mode under the cursor.</summary>
    public PanelSortMode Selected => Modes[_cursor];

    /// <summary>Whether Enter will apply the order reversed.</summary>
    public bool Descending { get; private set; }

    /// <summary>Sets the panel to sort; call before showing the dialog.</summary>
    public void Load(IContentModel? target)
    {
        _target = target;
        var current = (target as ISortableContentModel)?.Sort ?? PanelSort.AsListed;
        _cursor = Math.Max(0, Modes.ToList().IndexOf(current.Mode));
        Descending = current.Descending;
        UpdateLines();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(34, Modes.Count + 6));
        }

        _lines.Position = new Position(2, 1);
        _lines.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), Modes.Count + 2);
        base.OnInitializeLayout();
        UpdateLines();
    }

    private static char LetterOf(PanelSortMode mode) => char.ToLowerInvariant(PanelSort.DisplayName(mode)[0]);

    private void UpdateLines()
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var applied = (_target as ISortableContentModel)?.Sort ?? PanelSort.AsListed;
        var lines = new List<InfoLines.Line>();
        for (var i = 0; i < Modes.Count; i++)
        {
            var mode = Modes[i];
            // The arrow is the cursor; the dot is the order the panel is in now.
            var text = $"{(i == _cursor ? "▶" : " ")} {PanelSort.DisplayName(mode),-12}{(mode == applied.Mode ? "•" : " ")}  {LetterOf(mode)}";
            lines.Add(new(text, i == _cursor ? Color.Yellow : null));
        }

        lines.Add(default);
        lines.Add(new($"  [{(Descending ? "x" : " ")}] Reversed       r", colorScheme.Foreground));
        _lines.Lines = lines;
        StateHasChanged();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Up/Down", UiText.Get("DialogHint.MoveOverSortModes", "Move the cursor over the sort modes"));
        yield return ("Home/End", UiText.Get("DialogHint.JumpFirstLastMode", "Jump to the first or last mode"));
        yield return ("Enter", UiText.Get("DialogHint.SortByModeUnderCursor", "Sort by the mode under the cursor"));
        yield return ("Letter", UiText.Get("DialogHint.SortByLetterAtOnce", "Sort by the mode marked with that letter at once"));
        yield return ("R/Space", UiText.Get("DialogHint.ReverseSortOrder", "Reverse the sort order"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!keyEvent.Alt && !keyEvent.Control)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    _cursor = Math.Max(0, _cursor - 1);
                    UpdateLines();
                    return true;
                case ConsoleKey.DownArrow:
                    _cursor = Math.Min(Modes.Count - 1, _cursor + 1);
                    UpdateLines();
                    return true;
                case ConsoleKey.Home:
                    _cursor = 0;
                    UpdateLines();
                    return true;
                case ConsoleKey.End:
                    _cursor = Modes.Count - 1;
                    UpdateLines();
                    return true;
                case ConsoleKey.Enter:
                    Apply();
                    return true;
                case ConsoleKey.R:
                case ConsoleKey.Spacebar:
                    Descending = !Descending;
                    UpdateLines();
                    return true;
            }

            var letter = char.ToLowerInvariant(keyEvent.KeyChar);
            for (var i = 0; i < Modes.Count; i++)
            {
                if (letter == LetterOf(Modes[i]))
                {
                    _cursor = i;
                    Apply();
                    return true;
                }
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// A click on an order applies it, as its letter does; a click on the Reversed line flips it and leaves the dialog
    /// open, as <c>r</c> does; the wheel moves the cursor. Before this the dialog could only be driven from the keyboard.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            _cursor = Math.Clamp(_cursor + (mouseEvent.WheelDelta > 0 ? -1 : 1), 0, Modes.Count - 1);
            UpdateLines();
            return true;
        }

        if (mouseEvent.LeftClicked
            && (long)mouseEvent.Position.X >= (long)_lines.AbsolutePosition.X
            && (long)mouseEvent.Position.X < (long)_lines.AbsolutePosition.X + _lines.ViewSize.Width)
        {
            var row = (int)((long)mouseEvent.Position.Y - (long)_lines.AbsolutePosition.Y);
            if (row >= 0 && row < Modes.Count)
            {
                _cursor = row;
                Apply();
                return true;
            }

            // The orders, a blank line, then the Reversed line.
            if (row == Modes.Count + 1)
            {
                Descending = !Descending;
                UpdateLines();
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>Puts the panel in the chosen order, saves it for the panel, and closes.</summary>
    public void Apply()
    {
        var sort = new PanelSort(Selected, Selected != PanelSortMode.AsListed && Descending);
        if (_target is ISortableContentModel { CanSort: true } sortable)
        {
            sortable.SetSort(sort);
            _settingsService?.Set(PanelSort.SettingsKey(_target.Name), sort.Serialize());
        }

        Close();
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var footer = " Enter:apply  Esc:close ".FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}
