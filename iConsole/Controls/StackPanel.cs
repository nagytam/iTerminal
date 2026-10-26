using iConsole.Data;
using iConsole.Display;
using iConsole.Layouts;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// Lays its children out in a row (<see cref="Orientation.Horizontal"/>) or a column (<see cref="Orientation.Vertical"/>),
/// each sized by a <see cref="SizeRule"/>: a fixed number of cells, a percentage of the panel, or a weighted share of what
/// is left. The sizes are worked out again whenever the panel is drawn, so a resize needs no code from the owner.
/// Every child spans the whole width of the panel across the axis. A child that is not visible takes no space and no gap.
/// The panel is not focusable; Tab moves to whatever it contains.
/// </summary>
public class StackPanel : Control
{
    private readonly List<(IControl Control, SizeRule Rule)> _items = [];
    private Orientation _orientation = Orientation.Vertical;
    private int _gap;

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

    public IReadOnlyList<IControl> Items => [.. _items.Select(item => item.Control)];

    /// <summary>Appends <paramref name="control"/>, sized by <paramref name="rule"/> (<see cref="SizeRule.Fill(double)"/> when omitted).</summary>
    public void Add(IControl control, SizeRule? rule = null)
    {
        _items.Add((control, rule ?? SizeRule.Fill()));
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
        int index = _items.FindIndex(item => ReferenceEquals(item.Control, control));
        if (index >= 0)
        {
            _items[index] = (control, rule);
            LayoutChildren();
            StateHasChanged();
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutChildren();
    }

    private void LayoutChildren()
    {
        bool row = _orientation == Orientation.Horizontal;
        int length = (int)(row ? ViewSize.Width : ViewSize.Height);
        int cross = (int)(row ? ViewSize.Height : ViewSize.Width);
        List<(IControl Control, SizeRule Rule)> shown = _items.Where(item => item.Control.IsVisible).ToList();
        int[] sizes = SizeSolver.Solve(length, shown.Select(item => item.Rule).ToList(), _gap);
        int offset = 0;
        for (int i = 0; i < shown.Count; i++)
        {
            IControl child = shown[i].Control;
            child.Position = row ? new Position(offset, 0) : new Position(0, offset);
            child.ViewSize = row ? new Size(sizes[i], cross) : new Size(cross, sizes[i]);
            offset += sizes[i] + _gap;
        }
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
