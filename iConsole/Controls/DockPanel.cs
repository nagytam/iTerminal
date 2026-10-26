using iConsole.Data;
using iConsole.Display;
using iConsole.Layouts;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>The edge of a <see cref="DockPanel"/> a child is attached to.</summary>
public enum DockSide
{
    Top,
    Bottom,
    Left,
    Right,

    /// <summary>Takes whatever space the earlier children left.</summary>
    Fill,
}

/// <summary>
/// Attaches children to the edges of the panel, in the order they were added: each takes a strip of the space the earlier
/// ones left, across the whole remaining width (top, bottom) or height (left, right). A child docked to
/// <see cref="DockSide.Fill"/> takes what is left; children added after it get no space. The strip's thickness is a
/// <see cref="SizeRule"/> (one cell when omitted) applied to the space left at that point, so a menu bar on top, a status bar
/// at the bottom, a tree on the left and an editor that fills the rest is four calls. The layout is worked out again on every
/// draw. A child that is not visible takes no space and no gap. The panel is not focusable; Tab moves to whatever it contains.
/// </summary>
public class DockPanel : Control
{
    private readonly List<(IControl Control, DockSide Side, SizeRule Rule)> _items = [];
    private int _gap;

    public DockPanel(IThemeService themeService) : base(themeService)
    {
        CanFocus = false;
        ViewSize = new Size(30, 10);
    }

    /// <summary>Empty cells between a docked strip and what follows it. Negative values count as 0.</summary>
    public int Gap
    {
        get => _gap;
        set
        {
            int gap = Math.Max(0, value);
            if (gap != _gap)
            {
                _gap = gap;
                Relayout();
            }
        }
    }

    public IReadOnlyList<IControl> Items => [.. _items.Select(item => item.Control)];

    /// <summary>Docks <paramref name="control"/> to <paramref name="side"/>; <paramref name="size"/> is its thickness (ignored for <see cref="DockSide.Fill"/>).</summary>
    public void Add(IControl control, DockSide side, SizeRule? size = null)
    {
        _items.Add((control, side, size ?? SizeRule.Fixed(1)));
        AddControl(control);
        Relayout();
    }

    public void Remove(IControl control)
    {
        if (_items.RemoveAll(item => ReferenceEquals(item.Control, control)) > 0)
        {
            RemoveControl(control);
            Relayout();
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutChildren();
    }

    private void Relayout()
    {
        LayoutChildren();
        StateHasChanged();
    }

    private void LayoutChildren()
    {
        int left = 0;
        int top = 0;
        int width = (int)ViewSize.Width;
        int height = (int)ViewSize.Height;
        bool filled = false;
        foreach ((IControl control, DockSide side, SizeRule rule) in _items)
        {
            if (!control.IsVisible)
            {
                continue;
            }

            if (filled)
            {
                control.ViewSize = new Size(0, 0);
                continue;
            }

            if (side == DockSide.Fill)
            {
                control.Position = new Position(left, top);
                control.ViewSize = new Size(width, height);
                filled = true;
                continue;
            }

            bool horizontal = side is DockSide.Left or DockSide.Right;
            int available = horizontal ? width : height;
            int thickness = SizeSolver.Solve(available, [rule])[0];
            int rest = Math.Max(0, available - thickness - (thickness > 0 ? _gap : 0));
            switch (side)
            {
                case DockSide.Top:
                    control.Position = new Position(left, top);
                    control.ViewSize = new Size(width, thickness);
                    top += available - rest;
                    height = rest;
                    break;
                case DockSide.Bottom:
                    control.Position = new Position(left, top + height - thickness);
                    control.ViewSize = new Size(width, thickness);
                    height = rest;
                    break;
                case DockSide.Left:
                    control.Position = new Position(left, top);
                    control.ViewSize = new Size(thickness, height);
                    left += available - rest;
                    width = rest;
                    break;
                default:
                    control.Position = new Position(left + width - thickness, top);
                    control.ViewSize = new Size(thickness, height);
                    width = rest;
                    break;
            }
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
