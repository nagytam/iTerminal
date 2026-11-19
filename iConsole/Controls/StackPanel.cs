using iConsole.Data;
using iConsole.Display;
using iConsole.Layouts;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>Where a child sits across the stack's axis, in the room it has: the full width of a column, the full height of a row.</summary>
public enum StackAlignment
{
    /// <summary>Fills the room (the default).</summary>
    Stretch,

    /// <summary>At the start (left of a column, top of a row), as big as the child's own size across.</summary>
    Start,

    /// <summary>In the middle, as big as the child's own size across.</summary>
    Center,

    /// <summary>At the end (right of a column, bottom of a row), as big as the child's own size across.</summary>
    End,
}

/// <summary>How the children share the room left over along the axis when their sizes do not fill it.</summary>
public enum StackJustify
{
    /// <summary>All at the start; the room is left at the end (the default).</summary>
    Start,

    /// <summary>All together in the middle.</summary>
    Center,

    /// <summary>All together at the end.</summary>
    End,

    /// <summary>The first at the start, the last at the end, the room shared between neighbours.</summary>
    SpaceBetween,

    /// <summary>The room shared round every child, half of a share at each end.</summary>
    SpaceAround,

    /// <summary>The room shared equally between children and at both ends.</summary>
    SpaceEvenly,
}

/// <summary>
/// Lays its children out in a row (<see cref="Orientation.Horizontal"/>) or a column (<see cref="Orientation.Vertical"/>),
/// each sized by a <see cref="SizeRule"/>: a fixed number of cells, a percentage of the panel, or a weighted share of what
/// is left. The sizes are worked out again whenever the panel is drawn, so a resize needs no code from the owner.
/// Across the axis a child fills the panel unless it has an alignment (<see cref="CrossAlignment"/>,
/// <see cref="SetAlignment"/>): then it keeps the size it had when the alignment was set and sits at the start, the middle or the
/// end. Along the axis, room left by fixed and percentage children is shared out by <see cref="Justify"/>; with a fill child there
/// is none. A child that is not visible takes no space and no gap. The panel is not focusable; Tab moves to whatever it contains.
/// </summary>
public class StackPanel : Control
{
    private sealed class Item(IControl control, SizeRule rule)
    {
        public IControl Control { get; } = control;

        public SizeRule Rule { get; set; } = rule;

        /// <summary>Null follows <see cref="CrossAlignment"/>.</summary>
        public StackAlignment? Alignment { get; set; }

        /// <summary>The child's own size across the axis, taken when it was added or its alignment was set.</summary>
        public int NaturalCross { get; set; }

        /// <summary>The child's own size along the axis, taken when it was added. A fill child keeps it when the panel wraps.</summary>
        public int NaturalMain { get; set; }
    }

    private readonly List<Item> _items = [];
    private Orientation _orientation = Orientation.Vertical;
    private int _gap;
    private StackAlignment _crossAlignment = StackAlignment.Stretch;
    private StackJustify _justify = StackJustify.Start;
    private bool _wrap;

    public StackPanel(IThemeService themeService) : base(themeService)
    {
        CanFocus = false;
        ViewSize = new Size(30, 10);
    }

    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                LayoutChildren();
                StateHasChanged();
            }
        }
    }

    /// <summary>Empty cells between neighbouring children. Negative values count as 0.</summary>
    public int Gap
    {
        get => _gap;
        set
        {
            int gap = Math.Max(0, value);
            if (gap != _gap)
            {
                _gap = gap;
                LayoutChildren();
                StateHasChanged();
            }
        }
    }

    /// <summary>Where children sit across the axis unless they say otherwise. <see cref="StackAlignment.Stretch"/> fills the room.</summary>
    public StackAlignment CrossAlignment
    {
        get => _crossAlignment;
        set
        {
            if (_crossAlignment != value)
            {
                _crossAlignment = value;
                LayoutChildren();
                StateHasChanged();
            }
        }
    }

    /// <summary>How the room left along the axis is shared out. Only fixed and percentage children leave any.</summary>
    public StackJustify Justify
    {
        get => _justify;
        set
        {
            if (_justify != value)
            {
                _justify = value;
                LayoutChildren();
                StateHasChanged();
            }
        }
    }

    /// <summary>
    /// When true, a child that would overflow the current line starts a new one: a row wraps into more rows, a column into more
    /// columns. Each line is as thick as its thickest child, and <see cref="CrossAlignment"/> and <see cref="SetAlignment"/> place
    /// the children within it. A fixed or percent child is sized as it would be in an unwrapped panel (a percent of the whole
    /// panel), and a fill child keeps the size it had when it was added, since a line has no shared room to divide. Lines past the
    /// far edge of the panel are clipped. Off by default.
    /// </summary>
    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (_wrap != value)
            {
                _wrap = value;
                LayoutChildren();
                StateHasChanged();
            }
        }
    }

    public IReadOnlyList<IControl> Items => [.. _items.Select(item => item.Control)];

    private int CrossOf(IControl control) => (int)(_orientation == Orientation.Horizontal ? control.ViewSize.Height : control.ViewSize.Width);

    private int MainOf(IControl control) => (int)(_orientation == Orientation.Horizontal ? control.ViewSize.Width : control.ViewSize.Height);

    /// <summary>Appends <paramref name="control"/>, sized by <paramref name="rule"/> (<see cref="SizeRule.Fill(double)"/> when omitted).</summary>
    public void Add(IControl control, SizeRule? rule = null, StackAlignment? alignment = null)
    {
        _items.Add(new Item(control, rule ?? SizeRule.Fill()) { Alignment = alignment, NaturalCross = CrossOf(control), NaturalMain = MainOf(control) });
        AddControl(control);
        LayoutChildren();
        StateHasChanged();
    }

    public void Remove(IControl control)
    {
        if (_items.RemoveAll(item => ReferenceEquals(item.Control, control)) > 0)
        {
            RemoveControl(control);
            LayoutChildren();
            StateHasChanged();
        }
    }

    /// <summary>Changes the rule of a child already added.</summary>
    public void SetRule(IControl control, SizeRule rule)
    {
        Item? item = _items.Find(i => ReferenceEquals(i.Control, control));
        if (item is not null)
        {
            item.Rule = rule;
            LayoutChildren();
            StateHasChanged();
        }
    }

    /// <summary>
    /// Sets where one child sits across the axis, and how big it is there: <paramref name="size"/>, or when omitted the size it has now.
    /// Null <paramref name="alignment"/> goes back to <see cref="CrossAlignment"/>.
    /// </summary>
    public void SetAlignment(IControl control, StackAlignment? alignment, int? size = null)
    {
        Item? item = _items.Find(i => ReferenceEquals(i.Control, control));
        if (item is not null)
        {
            item.Alignment = alignment;
            item.NaturalCross = Math.Max(0, size ?? CrossOf(control));
            LayoutChildren();
            StateHasChanged();
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutChildren();
    }

    /// <summary>The offset of the first of <paramref name="count"/> children and the gap before each, for the room left over.</summary>
    public static (int First, int[] Gaps) Distribute(StackJustify justify, int free, int count, int gap)
    {
        int[] gaps = new int[count];
        for (int i = 1; i < count; i++)
        {
            gaps[i] = gap;
        }

        if (count == 0 || free <= 0)
        {
            return (0, gaps);
        }

        switch (justify)
        {
            case StackJustify.Center:
                return (free / 2, gaps);
            case StackJustify.End:
                return (free, gaps);
            case StackJustify.SpaceBetween when count > 1:
                Share(gaps, free, 1, count - 1);
                return (0, gaps);
            case StackJustify.SpaceAround:
            {
                // Every child gets a share; half of it goes before the first and half after the last.
                int share = free / count;
                int first = share / 2;
                Share(gaps, free - (first * 2), 1, count - 1);
                return (first, gaps);
            }

            case StackJustify.SpaceEvenly:
            {
                int first = free / (count + 1);
                Share(gaps, free - (first * 2), 1, count - 1);
                return (first, gaps);
            }

            default:
                return (0, gaps);
        }
    }

    /// <summary>Adds <paramref name="total"/> to <paramref name="gaps"/> from <paramref name="start"/> on, spread over <paramref name="slots"/>, the first ones taking the remainder.</summary>
    private static void Share(int[] gaps, int total, int start, int slots)
    {
        if (slots <= 0)
        {
            return;
        }

        for (int i = 0; i < slots; i++)
        {
            gaps[start + i] += (total / slots) + (i < total % slots ? 1 : 0);
        }
    }

    private void LayoutChildren()
    {
        bool row = _orientation == Orientation.Horizontal;
        int length = (int)(row ? ViewSize.Width : ViewSize.Height);
        int cross = (int)(row ? ViewSize.Height : ViewSize.Width);
        List<Item> shown = _items.Where(item => item.Control.IsVisible).ToList();
        if (_wrap)
        {
            LayoutWrapped(row, length, shown);
            return;
        }

        int[] sizes = SizeSolver.Solve(length, shown.Select(item => item.Rule).ToList(), _gap);
        int free = length - sizes.Sum() - (Math.Max(0, shown.Count - 1) * _gap);
        (int offset, int[] gaps) = Distribute(_justify, free, shown.Count, _gap);
        for (int i = 0; i < shown.Count; i++)
        {
            Item item = shown[i];
            offset += gaps[i];
            StackAlignment alignment = item.Alignment ?? _crossAlignment;
            int size = alignment == StackAlignment.Stretch ? cross : Math.Min(item.NaturalCross, cross);
            int across = alignment switch
            {
                StackAlignment.Center => (cross - size) / 2,
                StackAlignment.End => cross - size,
                _ => 0,
            };
            item.Control.Position = row ? new Position(offset, across) : new Position(across, offset);
            item.Control.ViewSize = row ? new Size(sizes[i], size) : new Size(size, sizes[i]);
            offset += sizes[i];
        }
    }

    /// <summary>Breaks the shown children into lines along the axis, then places each line one thickness after the last.</summary>
    private void LayoutWrapped(bool row, int length, List<Item> shown)
    {
        List<List<Item>> lines = [];
        List<List<int>> lineSizes = [];
        List<Item> line = [];
        List<int> sizes = [];
        int used = 0;
        foreach (Item item in shown)
        {
            int size = WrappedSize(item, length);
            int needed = line.Count == 0 ? size : _gap + size;
            if (line.Count > 0 && used + needed > length)
            {
                lines.Add(line);
                lineSizes.Add(sizes);
                line = [];
                sizes = [];
                used = 0;
                needed = size;
            }

            line.Add(item);
            sizes.Add(size);
            used += needed;
        }

        if (line.Count > 0)
        {
            lines.Add(line);
            lineSizes.Add(sizes);
        }

        int lineStart = 0;
        for (int l = 0; l < lines.Count; l++)
        {
            List<Item> items = lines[l];
            List<int> itemSizes = lineSizes[l];
            int thickness = items.Max(item => item.NaturalCross);
            int free = length - itemSizes.Sum() - (Math.Max(0, items.Count - 1) * _gap);
            (int offset, int[] gaps) = Distribute(_justify, free, items.Count, _gap);
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                offset += gaps[i];
                StackAlignment alignment = item.Alignment ?? _crossAlignment;
                int size = alignment == StackAlignment.Stretch ? thickness : Math.Min(item.NaturalCross, thickness);
                int across = alignment switch
                {
                    StackAlignment.Center => (thickness - size) / 2,
                    StackAlignment.End => thickness - size,
                    _ => 0,
                };
                int along = lineStart + across;
                item.Control.Position = row ? new Position(offset, along) : new Position(along, offset);
                item.Control.ViewSize = row ? new Size(itemSizes[i], size) : new Size(size, itemSizes[i]);
                offset += itemSizes[i];
            }

            lineStart += thickness + _gap;
        }
    }

    /// <summary>A child's size along the axis when the panel wraps: its fixed or percent size, or its natural size for a fill child.</summary>
    private static int WrappedSize(Item item, int length)
    {
        SizeRule rule = item.Rule;
        int size = rule.Kind switch
        {
            SizeRuleKind.Fixed => (int)Math.Min(rule.Value, length),
            SizeRuleKind.Percent => (int)Math.Floor(length * Math.Min(rule.Value, 100) / 100),
            _ => item.NaturalMain,
        };
        return Math.Min(length, Math.Clamp(size, rule.Min, Math.Max(rule.Min, rule.Max)));
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        iConsole.Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, ThemeService.CurrentColorScheme);
        LayoutChildren();
        base.Draw(buffer);
    }
}
