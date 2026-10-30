using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls;

public class ListBox<T> : Control
{
    public ListBox(IListBoxModel<T> listModel, IThemeService themeService)
        : base(themeService)
    {
        ListModel = listModel;
        ScrollableModel = new ScrollableModel();
        CanFocus = true;

        if (ListModel is INotifyPropertyChanged propertyChanged)
        {
            propertyChanged.PropertyChanged += OnListModelPropertyChanged;
        }

        ScrollableModel.ScrollableSize = new Size(ViewSize.Width, ListModel.ListItems.Count);

        AddCommand(new ScrollDownCommand(ScrollableModel));
        AddCommand(new ScrollUpCommand(ScrollableModel));
        AddCommand(new ScrollPageDownCommand(ScrollableModel));
        AddCommand(new ScrollPageUpCommand(ScrollableModel));
        AddCommand(new ScrollToHomeVerticalCommand(ScrollableModel));
        AddCommand(new ScrollToEndVerticalCommand(ScrollableModel));
        AddCommand(new InvertSelectionCommand<T>(ScrollableModel, ListModel));
        _selectItemCommand = new SelectListItemCommand<T>(ScrollableModel, ListModel);
        AddCommand(_selectItemCommand);
    }

    /// <summary>Rows the wheel moves the cursor by in one notch.</summary>
    private const int WheelRows = 3;

    private readonly SelectListItemCommand<T> _selectItemCommand;

    public IListBoxModel<T> ListModel { get; }

    public ScrollableModel ScrollableModel { get; }

    public IListItem<T>? SelectedItem => ListModel.SelectedItem;

    public int Count => ListModel.ListItems.Count;

    /// <summary>
    /// Drawn on the first row, in the disabled colours, while the list has no items — so an empty
    /// list says so instead of looking like a panel that has not finished loading. Empty by default.
    /// </summary>
    public string EmptyText { get; set; } = string.Empty;

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        ScrollableModel.ViewSize = ViewSize;
        ScrollableModel.ScrollableSize = new Size(ViewSize.Width, ListModel.ListItems.Count);
    }

    private void OnListModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IListBoxModel<T>.ListItems) or nameof(IListBoxModel<T>.Items) or nameof(IListBoxModel<T>.HasPendingUpdate))
        {
            ScrollableModel.ScrollableSize = new Size(ViewSize.Width, ListModel.ListItems.Count);
            ScrollableModel.Update();
            StateHasChanged();
        }
    }

    /// <summary>
    /// Rebuilds the rows even though the items did not change — for when the filter (or anything
    /// else the rows are projected through) did. <see cref="UpdateModel"/> alone does nothing then.
    /// </summary>
    public void RefreshItems()
    {
        ListModel.Invalidate();
        UpdateModel();
    }

    public void UpdateModel()
    {
        ListModel.UpdateListItems();
        ScrollableModel.ScrollableSize = new Size(ViewSize.Width, ListModel.ListItems.Count);
        ScrollableModel.Update();
        StateHasChanged();
    }

    /// <summary>
    /// Opt-in incremental search: typing letters moves the cursor to the next row whose text starts with
    /// them (typed within <see cref="TypeAheadTimeout"/> of each other they build a longer prefix, a repeated
    /// single letter cycles through the rows it starts). Off by default because lists in dialogs share the
    /// letter keys with the dialog's own hot keys.
    /// </summary>
    public bool EnableTypeAhead { get; set; }

    /// <summary>How long a pause starts a new prefix.</summary>
    public TimeSpan TypeAheadTimeout { get; set; } = TimeSpan.FromMilliseconds(900);

    private string _typeAheadPrefix = string.Empty;
    private long _typeAheadTick;

    /// <summary>
    /// The index of the row to move to for <paramref name="prefix"/>, searching from <paramref name="start"/>
    /// (inclusive) and wrapping; leading punctuation and spaces (markers like "● ") are ignored. -1 for none.
    /// </summary>
    public static int FindPrefix(IReadOnlyList<string> values, string prefix, int start)
    {
        prefix ??= string.Empty;
        for (var offset = 0; offset < values.Count; offset++)
        {
            // A start before the first row (or past the last) is taken modulo the count instead of indexing out of range.
            var index = (((start + offset) % values.Count) + values.Count) % values.Count;
            var text = values[index].TrimStart(' ', '●', '○', '▶', '>', '*', '-', '•');
            if (text.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled || Count == 0)
        {
            yield break;
        }

        yield return ("Up/Down", UiText.Get("Hint.MoveCursor", "Move the cursor"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.MoveCursorByPage", "Move the cursor by a page"));
        yield return ("Home/End", UiText.Get("Hint.FirstOrLastItem", "First or last item"));
        yield return ("Enter", UiText.Get("Hint.SelectItemUnderCursor", "Select the item under the cursor"));
        yield return ("Insert", UiText.Get("Hint.MarkItemUnderCursor", "Mark or unmark the item under the cursor"));
        if (EnableTypeAhead)
        {
            yield return ("Letters", UiText.Get("Hint.JumpToLetter", "Jump to the next item that starts with them"));
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !EnableTypeAhead || !IsFocused || !IsEnabled || Count == 0
            || keyEvent.Control || keyEvent.Alt || !char.IsLetterOrDigit(keyEvent.KeyChar))
        {
            return isHandled;
        }

        var now = Environment.TickCount64;
        var continuing = _typeAheadPrefix.Length > 0 && now - _typeAheadTick < TypeAheadTimeout.TotalMilliseconds;
        _typeAheadTick = now;
        var typed = keyEvent.KeyChar.ToString();
        var cursor = (int)ScrollableModel.CursorPosition.Y;
        var values = ListModel.ListItems.Select(item => item?.Value ?? string.Empty).ToList();

        // A letter typed again, on its own, steps to the next row with that letter rather than searching for "aa".
        var repeating = continuing && _typeAheadPrefix.Length == 1 && string.Equals(_typeAheadPrefix, typed, StringComparison.CurrentCultureIgnoreCase);
        _typeAheadPrefix = continuing && !repeating ? _typeAheadPrefix + typed : typed;

        var index = FindPrefix(values, _typeAheadPrefix, repeating || _typeAheadPrefix.Length == 1 ? cursor + 1 : cursor);
        if (index < 0)
        {
            return false;
        }

        ScrollableModel.CursorPosition.Y = index;
        ScrollableModel.ScrollToVisible();
        StateHasChanged();
        return true;
    }

    /// <summary>
    /// A click on a row moves the cursor there and selects it, as Enter does on the cursor row, and
    /// takes the keyboard focus. The wheel moves the cursor. Before this a list could only be driven
    /// from the keyboard: a mouse click on a row did nothing, and the wheel scrolled the dialog's
    /// neighbours instead of the list.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var isHandled = await base.HandleMouseEventAsync(mouseEvent);

        var relativeX = (long)mouseEvent.Position.X - (long)AbsolutePosition.X;
        var relativeY = (long)mouseEvent.Position.Y - (long)AbsolutePosition.Y;
        var isInside = relativeX >= 0 && relativeX < ViewSize.Width
            && relativeY >= 0 && relativeY < ViewSize.Height;
        if (!isInside || Count == 0)
        {
            return isHandled;
        }

        if (mouseEvent.WheelDelta != 0)
        {
            var target = ScrollableModel.CursorPosition.Y + (mouseEvent.WheelDelta > 0 ? -WheelRows : WheelRows);
            ScrollableModel.CursorPosition.Y = Math.Clamp(target, 0, Count - 1);
            ScrollableModel.ScrollToVisible();
            StateHasChanged();
            return true;
        }

        if ((mouseEvent.LeftClicked || mouseEvent.RightClicked) && IsEnabled)
        {
            var index = ScrollableModel.ScrollPosition.Y + relativeY;
            if (index < Count)
            {
                if (!IsFocused)
                {
                    SetFocus();
                }
                ScrollableModel.CursorPosition.Y = index;
                ScrollableModel.ScrollToVisible();
                await _selectItemCommand.RunAsync(null);
                StateHasChanged();
                return true;
            }
        }

        return isHandled;
    }

    public override void OnIdle()
    {
        base.OnIdle();

        if (!ListModel.HasPendingUpdate)
        {
            return;
        }

        ListModel.UpdateListItems();
        ScrollableModel.ScrollableSize = new Size(ViewSize.Width, ListModel.ListItems.Count);
        ScrollableModel.Update();
        StateHasChanged();
    }

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        base.Draw(buffer);
        var filteredItems = ListModel.ListItems.ToList();
        var width = (int)ViewSize.Width;
        var emptyLines = filteredItems.Count == 0 ? WrapEmptyText(EmptyText, width) : [];

        // Every row of the control is painted, including the ones below the last item: nothing
        // clears a layer between frames, so a list that shrinks (a filter, a refresh, a deletion)
        // would otherwise keep showing the rows it no longer has.
        for (int y = 0; y < ViewSize.Height; y++)
        {
            var index = (int)(y + ScrollableModel.ScrollPosition.Y);
            string text;
            Color foregroundColor;
            Color backgroundColor;
            if (index < filteredItems.Count)
            {
                var item = filteredItems[index];
                text = item?.Value ?? string.Empty;
                var visualState = GetVisualState(index);
                GetColor(colorScheme, visualState, out foregroundColor, out backgroundColor);
                if (visualState is VisualState.Normal or VisualState.Disabled)
                {
                    foregroundColor = GetItemForegroundOverride(index) ?? foregroundColor;
                }
            }
            else
            {
                text = y < emptyLines.Count ? emptyLines[y] : string.Empty;
                foregroundColor = text.Length > 0 ? colorScheme.DisabledForeground : colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
            }

            var textString = new ConxelString(text.PadRight(width), foregroundColor, backgroundColor);
            buffer.Write(ZOrder,
                new Position(AbsolutePosition.X, (uint)(AbsolutePosition.Y + y)), ViewSize,
                textString);
        }
    }

    /// <summary>
    /// <see cref="EmptyText"/> word-wrapped to the list's width, keeping its indent on every line.
    /// It used to be drawn on the first row alone and cut at the edge, which in a narrow dialog took
    /// the end of the message — usually the part that says what to do ("…unlo").
    /// </summary>
    private static List<string> WrapEmptyText(string text, int width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0)
        {
            return [];
        }
        var indent = text.Length - text.TrimStart(' ').Length;
        if (text.Length <= width || indent >= width / 2)
        {
            return [text];
        }
        var prefix = new string(' ', indent);
        return [.. text.TrimStart(' ').WrapWords(width - indent).Select(line => prefix + line)];
    }

    /// <summary>
    /// Lets a subclass recolor a non-selected row's foreground based on the underlying item (e.g. a
    /// git status color or diff-line syntax color) without having to reimplement <see cref="Draw"/>.
    /// Only consulted for <see cref="VisualState.Normal"/>/<see cref="VisualState.Disabled"/> rows so
    /// the selection highlight always wins, matching how per-row coloring worked before this hook existed.
    /// </summary>
    protected virtual Color? GetItemForegroundOverride(int index) => null;

    public virtual VisualState GetVisualState(int index)
    {
        try
        {
            var item = ListModel.ListItems[index];
            if (index == ScrollableModel.CursorPosition.Y)
            {
                return item.IsEnabled ? VisualState.Selected : VisualState.SelectedDisabled;
            }
            else
            {
                return item.IsEnabled ? VisualState.Normal : VisualState.Disabled;
            }
        }
        catch
        {
            return VisualState.Disabled;
        }
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        switch (visualState)
        {
            case VisualState.Disabled:
                foregroundColor = colorScheme.DisabledForeground;
                backgroundColor = colorScheme.DisabledBackground;
                break;
            case VisualState.Focused:
                foregroundColor = colorScheme.SelectedFocusedForeground;
                backgroundColor = colorScheme.SelectedFocusedBackground;
                break;
            case VisualState.Selected:
                // The cursor row adopts the theme's focused-selection highlight only while the
                // list itself holds keyboard focus, so the user can see which list is active when
                // several share a dialog. An unfocused list keeps the quieter selection colors.
                if (IsFocused)
                {
                    foregroundColor = colorScheme.SelectedFocusedForeground;
                    backgroundColor = colorScheme.SelectedFocusedBackground;
                }
                else
                {
                    foregroundColor = colorScheme.SelectedForeground;
                    backgroundColor = colorScheme.SelectedBackground;
                }
                break;
            case VisualState.SelectedDisabled:
                foregroundColor = colorScheme.DisabledSelectedForeground;
                backgroundColor = colorScheme.DisabledBackground;
                break;
            default:
                foregroundColor = colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
                break;
        }
    }
}
