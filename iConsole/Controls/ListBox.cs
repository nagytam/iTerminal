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
        AddCommand(new SelectListItemCommand<T>(ScrollableModel, ListModel));
    }

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
