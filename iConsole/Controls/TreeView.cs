using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;

namespace iConsole.Controls;

/// <summary>
/// A TreeView control for displaying hierarchical data in a tree structure.
/// </summary>
public class TreeView : Control
{
    /// <summary>
    /// Gets or sets the root node of the tree.
    /// </summary>
    public IHierarchicalItemModel Root { get; set; }
        = new HierarchicalItemModel(null, string.Empty);

    private bool _isInitializing;

    /// <summary>
    /// Initializes a new instance of the TreeView class.
    /// </summary>
    /// <param name="root">The root hierarchical item model for the tree.</param>
    /// <param name="themeService">The theme service for styling.</param>
    public TreeView(IHierarchicalItemModel root, IThemeService themeService) : base(themeService)
    {
        _isInitializing = true;
        Root = root;
        Root.PropertyChanged += OnPropertyChanged;
        AddCommand(new TreeScrollDownCommand(Root));
        AddCommand(new TreeScrollUpCommand(Root));
        AddCommand(new TreeScrollRightCommand(Root));
        AddCommand(new TreeScrollLeftCommand(Root));
        _isInitializing = false;
        CanFocus = true;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        if (IsVisible)
        {
            OnInitializeLayout();
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        Root.Position = new Position(AbsolutePosition.X, AbsolutePosition.Y);
        Root.Size = new Size(Root.Name.Length + 2, 1);
        var position = new Position(AbsolutePosition.X, AbsolutePosition.Y);
        Layout(Root, ref position);
    }

    private static void Layout(IHierarchicalItemModel root, ref Position position)
    {
        if (root.IsOpened)
        {
            ++position.X;
            foreach (var child in root.Children.AsSnapshot())
            {
                child.Position = new(position.X, ++position.Y);
                // include room for prefix (spaces + arrow) when drawing
                child.Size = new Size(child.Name.Length + 4, 1);
                var positionX = position.X;
                Layout(child, ref position);
                position.X = positionX;
            }
        }
    }

    /// <summary>
    /// Gets all items in the tree in a flat list, including all visible descendants.
    /// </summary>
    /// <returns>An enumerable of all hierarchical items.</returns>
    public IEnumerable<IHierarchicalItemModel> GetAllItems()
    {
        return GetAllItemsRecursive(Root);
    }

    private static IEnumerable<IHierarchicalItemModel> GetAllItemsRecursive(IHierarchicalItemModel item)
    {
        yield return item;
        if (item.IsOpened)
        {
            foreach (var child in item.Children.AsSnapshot())
            {
                foreach (var descendant in GetAllItemsRecursive(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>
    /// Gets the focused item in the tree, or null if no item is focused.
    /// </summary>
    /// <returns>The focused hierarchical item, or null.</returns>
    public IHierarchicalItemModel? GetFocusedItem()
    {
        return GetAllItems().FirstOrDefault(item => item.IsFocused);
    }

    /// <summary>
    /// Expands all nodes in the tree.
    /// </summary>
    public void ExpandAll()
    {
        ExpandAllRecursive(Root);
    }

    private static void ExpandAllRecursive(IHierarchicalItemModel item)
    {
        if (item.Children.Any())
        {
            item.IsOpened = true;
            foreach (var child in item.Children.AsSnapshot())
            {
                ExpandAllRecursive(child);
            }
        }
    }

    /// <summary>
    /// Collapses all nodes in the tree.
    /// </summary>
    public void CollapseAll()
    {
        CollapseAllRecursive(Root);
    }

    private static void CollapseAllRecursive(IHierarchicalItemModel item)
    {
        if (item.Children.Any())
        {
            item.IsOpened = false;
            foreach (var child in item.Children.AsSnapshot())
            {
                CollapseAllRecursive(child);
            }
        }
    }

    /// <summary>
    /// Toggles the expanded/collapsed state of a specific item.
    /// </summary>
    /// <param name="item">The item to toggle.</param>
    public void ToggleItem(IHierarchicalItemModel item)
    {
        if (item.Children.Any())
        {
            item.IsOpened = !item.IsOpened;
        }
    }

    /// <summary>
    /// Gets the count of visible nodes in the tree.
    /// </summary>
    /// <returns>The number of visible items.</returns>
    public int GetVisibleItemCount()
    {
        return GetAllItems().Count();
    }

    /// <summary>
    /// Gets the depth (nesting level) of a specific item.
    /// </summary>
    /// <param name="item">The item to get the depth for.</param>
    /// <returns>The depth of the item, starting from 0 for the root.</returns>
    public int GetItemDepth(IHierarchicalItemModel item)
    {
        int depth = 0;
        var current = item.Parent;
        while (current != null)
        {
            depth++;
            current = current.Parent;
        }
        return depth;
    }

    /// <summary>Glyph before an expanded node that has children.</summary>
    public const string ExpandedGlyph = "▾";

    /// <summary>Glyph before a collapsed node that has children.</summary>
    public const string CollapsedGlyph = "▸";

    /// <summary>Columns each nesting level is indented by.</summary>
    public const int IndentWidth = 2;

    /// <summary>Index, among the visible rows, of the first row drawn.</summary>
    public int ScrollOffset { get; private set; }

    /// <summary>
    /// The text of one row: the indent for its depth, the expand/collapse glyph (a blank for a leaf,
    /// so leaf names line up with their siblings'), and the name.
    /// </summary>
    public static string FormatRow(IHierarchicalItemModel item, int depth)
    {
        var glyph = item.Children.Any()
            ? (item.IsOpened ? ExpandedGlyph : CollapsedGlyph)
            : " ";
        return new string(' ', depth * IndentWidth) + glyph + " " + item.Name;
    }

    /// <summary>
    /// Draws the visible nodes as rows, scrolled so the focused node is on screen.
    ///
    /// <para>Every row of the control is painted — the rows past the last node included — because
    /// nothing clears a layer between frames: collapsing a node used to leave its children drawn
    /// underneath until something else happened to repaint that part of the screen. And the tree
    /// scrolls, because a folder tree is routinely taller than the page it sits on: drawn from
    /// the root down with no offset, moving the cursor past the bottom row moved it off screen.</para>
    /// </summary>
    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        var scheme = ThemeService.CurrentColorScheme;
        var rows = GetVisibleRows();
        var height = (int)ViewSize.Height;
        var width = (int)ViewSize.Width;
        EnsureFocusedRowVisible(rows, height);

        for (var y = 0; y < height; y++)
        {
            var index = ScrollOffset + y;
            string text;
            var foreground = scheme.Foreground;
            var background = scheme.Background;
            if (index < rows.Count)
            {
                var (item, depth) = rows[index];
                text = FormatRow(item, depth);
                if (item.IsFocused)
                {
                    // The same convention as ListBox: the strong highlight only while the tree
                    // itself has the keyboard, so the user can tell which control is active.
                    foreground = IsFocused ? scheme.SelectedFocusedForeground : scheme.SelectedForeground;
                    background = IsFocused ? scheme.SelectedFocusedBackground : scheme.SelectedBackground;
                }
            }
            else
            {
                text = string.Empty;
            }

            var line = new ConxelString(text.Length > width ? text[..width] : text.PadRight(width), foreground, background);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + (uint)y), new Size(width, 1), line);
        }
    }

    /// <summary>The visible nodes in drawing order, each with its depth below the root.</summary>
    public List<(IHierarchicalItemModel Item, int Depth)> GetVisibleRows()
    {
        var rows = new List<(IHierarchicalItemModel, int)>();
        AddVisibleRows(Root, 0, rows);
        return rows;
    }

    private static void AddVisibleRows(IHierarchicalItemModel item, int depth, List<(IHierarchicalItemModel, int)> rows)
    {
        rows.Add((item, depth));
        if (item.IsOpened)
        {
            foreach (var child in item.Children.AsSnapshot())
            {
                AddVisibleRows(child, depth + 1, rows);
            }
        }
    }

    private void EnsureFocusedRowVisible(List<(IHierarchicalItemModel Item, int Depth)> rows, int height)
    {
        var focused = rows.FindIndex(row => row.Item.IsFocused);
        if (focused >= 0)
        {
            if (focused < ScrollOffset)
            {
                ScrollOffset = focused;
            }
            else if (focused >= ScrollOffset + height)
            {
                ScrollOffset = focused - height + 1;
            }
        }
        ScrollOffset = Math.Clamp(ScrollOffset, 0, Math.Max(0, rows.Count - height));
    }
}
