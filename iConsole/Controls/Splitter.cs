using iConsole.Localization;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// Two panes with a draggable divider between them. <see cref="Orientation.Horizontal"/> puts the panes side by side
/// (a vertical divider); <see cref="Orientation.Vertical"/> stacks them (a horizontal divider).
/// The divider is a focus stop: the arrow keys along the split axis move it one cell, PageUp and PageDown eight,
/// Home puts it back in the middle. A left-button drag on the divider moves it too.
/// The position is kept as a <see cref="Ratio"/>, so the panes keep their proportions when the splitter is resized,
/// within the <see cref="MinFirstSize"/> and <see cref="MinSecondSize"/> limits.
/// </summary>
public class Splitter : Control
{
    private const int LargeStep = 8;

    private IControl? _first;
    private IControl? _second;
    private Orientation _orientation = Orientation.Horizontal;
    private double _ratio = 0.5;
    private int _minFirstSize;
    private int _minSecondSize;
    private bool _dragging;

    public Splitter(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(40, 10);
    }

    /// <summary>Raised when the divider moves, by key, drag or code.</summary>
    public event EventHandler? SplitChanged;

    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                LayoutPanes();
                StateHasChanged();
            }
        }
    }

    /// <summary>The share of the space (the divider excluded) the first pane gets, 0 to 1. NaN and out-of-range values are clamped.</summary>
    public double Ratio
    {
        get => _ratio;
        set
        {
            double clamped = double.IsNaN(value) ? 0.5 : Math.Clamp(value, 0, 1);
            if (clamped != _ratio)
            {
                _ratio = clamped;
                OnSplitChanged();
            }
        }
    }

    public int MinFirstSize
    {
        get => _minFirstSize;
        set
        {
            _minFirstSize = Math.Max(0, value);
            LayoutPanes();
            StateHasChanged();
        }
    }

    public int MinSecondSize
    {
        get => _minSecondSize;
        set
        {
            _minSecondSize = Math.Max(0, value);
            LayoutPanes();
            StateHasChanged();
        }
    }

    public IControl? First => _first;

    public IControl? Second => _second;

    public void SetFirst(IControl? pane) => _first = Replace(_first, pane);

    public void SetSecond(IControl? pane) => _second = Replace(_second, pane);

    /// <summary>The length of the first pane along the split axis, after the limits are applied.</summary>
    public int FirstSize => ComputeFirstSize(AxisLength, _ratio, _minFirstSize, _minSecondSize);

    /// <summary>Puts the divider so the first pane is <paramref name="size"/> cells long (clamped by the limits).</summary>
    public void SetFirstSize(int size)
    {
        int available = Math.Max(0, AxisLength - 1);
        int clamped = ClampFirst(size, available, _minFirstSize, _minSecondSize);
        Ratio = available == 0 ? 0.5 : (double)clamped / available;
    }

    /// <summary>The first pane's length for a splitter <paramref name="length"/> cells long, with one cell for the divider.</summary>
    public static int ComputeFirstSize(int length, double ratio, int minFirst, int minSecond)
    {
        int available = Math.Max(0, length - 1);
        int wanted = (int)Math.Round(Math.Clamp(double.IsNaN(ratio) ? 0.5 : ratio, 0, 1) * available);
        return ClampFirst(wanted, available, minFirst, minSecond);
    }

    private static int ClampFirst(int wanted, int available, int minFirst, int minSecond)
    {
        // Limits that cannot both be met share the space evenly rather than one pane taking all of it.
        if (minFirst + minSecond > available)
        {
            return available / 2;
        }

        return Math.Clamp(wanted, minFirst, available - minSecond);
    }

    private bool IsSideBySide => _orientation == Orientation.Horizontal;

    private int AxisLength => (int)(IsSideBySide ? ViewSize.Width : ViewSize.Height);

    private int CrossLength => (int)(IsSideBySide ? ViewSize.Height : ViewSize.Width);

    private IControl? Replace(IControl? old, IControl? replacement)
    {
        if (old is not null)
        {
            RemoveControl(old);
        }

        if (replacement is not null)
        {
            AddControl(replacement);
        }

        LayoutPanes();
        StateHasChanged();
        return replacement;
    }

    private void OnSplitChanged()
    {
        LayoutPanes();
        StateHasChanged();
        SplitChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutPanes();
    }

    private void LayoutPanes()
    {
        int first = FirstSize;
        int second = Math.Max(0, AxisLength - first - 1);
        int cross = Math.Max(0, CrossLength);
        if (_first is not null)
        {
            _first.Position = new Position(0, 0);
            _first.ViewSize = IsSideBySide ? new Size(first, cross) : new Size(cross, first);
        }

        if (_second is not null)
        {
            int offset = first + 1;
            _second.Position = IsSideBySide ? new Position(offset, 0) : new Position(0, offset);
            _second.ViewSize = IsSideBySide ? new Size(second, cross) : new Size(cross, second);
        }
    }

    /// <summary>The cell offset of the divider along the split axis.</summary>
    private int DividerOffset => FirstSize;

    /// <summary>A double arrow across the divider, and nothing special elsewhere.</summary>
    public override PointerShape PointerShapeAt(Position position)
    {
        return IsOnDivider(position)
            ? (IsSideBySide ? PointerShape.ResizeHorizontal : PointerShape.ResizeVertical)
            : PointerShape.Default;
    }

    private bool IsOnDivider(Position position)
    {
        int x = (int)position.X - (int)AbsolutePosition.X;
        int y = (int)position.Y - (int)AbsolutePosition.Y;
        if (x < 0 || y < 0 || x >= (int)ViewSize.Width || y >= (int)ViewSize.Height)
        {
            return false;
        }

        return (IsSideBySide ? x : y) == DividerOffset;
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled || keyEvent.Control || keyEvent.Alt)
        {
            return isHandled;
        }

        ConsoleKey less = IsSideBySide ? ConsoleKey.LeftArrow : ConsoleKey.UpArrow;
        ConsoleKey more = IsSideBySide ? ConsoleKey.RightArrow : ConsoleKey.DownArrow;
        int current = FirstSize;
        if (keyEvent.ConsoleKey == less)
        {
            SetFirstSize(current - 1);
        }
        else if (keyEvent.ConsoleKey == more)
        {
            SetFirstSize(current + 1);
        }
        else if (keyEvent.ConsoleKey == ConsoleKey.PageUp)
        {
            SetFirstSize(current - LargeStep);
        }
        else if (keyEvent.ConsoleKey == ConsoleKey.PageDown)
        {
            SetFirstSize(current + LargeStep);
        }
        else if (keyEvent.ConsoleKey == ConsoleKey.Home)
        {
            Ratio = 0.5;
        }
        else
        {
            return false;
        }

        return true;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_dragging)
        {
            if (mouseEvent.IsRelease || (!mouseEvent.IsDrag && !mouseEvent.LeftClicked))
            {
                _dragging = false;
                StateHasChanged();
                return true;
            }

            int offset = (int)(IsSideBySide ? mouseEvent.Position.X - AbsolutePosition.X : mouseEvent.Position.Y - AbsolutePosition.Y);
            SetFirstSize(offset);
            return true;
        }

        if (IsEnabled && IsVisible && mouseEvent.LeftClicked && !mouseEvent.IsRelease && !mouseEvent.IsDrag
            && IsOnDivider(mouseEvent.Position))
        {
            SetFocus();
            _dragging = true;
            StateHasChanged();
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return (IsSideBySide ? "Left/Right" : "Up/Down", UiText.Get("Hint.MoveDivider", "Move the divider"));
        yield return ("PgUp/PgDn", UiText.Get("Hint.MoveDividerBy8", "Move the divider by 8"));
        yield return ("Home", UiText.Get("Hint.CentreDivider", "Centre the divider"));
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        iConsole.Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, colorScheme);
        LayoutPanes();
        base.Draw(buffer);

        VisualState state = _dragging ? VisualState.Focused : GetVisualState();
        GetColor(colorScheme, state, out Color foreground, out Color background);
        bool emphasised = state == VisualState.Focused;
        int offset = DividerOffset;
        if (IsSideBySide)
        {
            string glyph = emphasised ? "┃" : Border.VerticalSingle;
            for (int y = 0; y < (int)ViewSize.Height; y++)
            {
                buffer.Write(ZOrder, new Position(AbsolutePosition.X + (uint)offset, AbsolutePosition.Y + (uint)y),
                    SizeHelper.Size1x1, new ConxelString(glyph, foreground, background));
            }
        }
        else
        {
            string glyph = emphasised ? "━" : Border.HorizontalSingle;
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + (uint)offset),
                new Size(ViewSize.Width, 1), new ConxelString(glyph.Repeat((int)ViewSize.Width), foreground, background));
        }
    }
}
