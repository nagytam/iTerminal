using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// Shows a window onto a child control that is larger than the space it is given, and scrolls it. The child is drawn
/// into a buffer of its own size and the part in view is copied to the screen, so it needs no scrolling support and
/// nothing it draws can leak outside the view.
/// <list type="bullet">
/// <item>Scroll with the arrow, Page, Home and End keys (Left and Right sideways) and the mouse wheel. Keys the content
/// handles itself, such as a list inside, go to the content first.</item>
/// <item>A scroll bar appears on the right edge and the bottom edge when the content does not fit in that direction
/// (<see cref="ShowScrollBars"/>); a click or a drag on it moves the view.</item>
/// <item>When the focus moves to a control inside the content, the view scrolls to show it.</item>
/// <item>A click inside the view is passed to the content in the content's own coordinates.</item>
/// </list>
/// The content's size is <see cref="ContentSize"/>, or its own <c>ViewSize</c> when that is not set. The whole content is
/// drawn on every repaint, so use <see cref="VirtualListBox{T}"/> or <see cref="VirtualTable{T}"/> for thousands of rows.
/// The view takes the keyboard focus itself only when the content has nothing to focus; otherwise Tab goes to the
/// content's controls.
/// </summary>
public class ScrollView : Control
{
    /// <summary>Rows a wheel notch moves the view by.</summary>
    public const int WheelStep = 3;

    private const string ThumbChar = "█";
    private const string TrackChar = "░";

    private IControl? _content;
    private Size? _contentSize;
    private bool _fitWidth;
    private bool _showScrollBars = true;
    private long _offsetX;
    private long _offsetY;
    private IControl? _lastFocused;
    private OffscreenBuffer? _offscreen;

    public ScrollView(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(30, 10);
    }

    /// <summary>The control being scrolled. Setting it replaces the previous one.</summary>
    public IControl? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value))
            {
                return;
            }

            if (_content is not null)
            {
                RemoveControl(_content);
            }

            _content = value;
            _lastFocused = null;
            if (value is not null)
            {
                AddControl(value);
            }

            ScrollTo(0, 0);
            UpdateFocusability();
            StateHasChanged();
        }
    }

    /// <summary>The size of the content. Null means the content's own <c>ViewSize</c>.</summary>
    public Size? ContentSize
    {
        get => _contentSize;
        set
        {
            _contentSize = value;
            ClampOffsets();
            StateHasChanged();
        }
    }

    /// <summary>The content is as wide as the view (less the vertical bar), so only height scrolls. <see cref="ContentSize"/> then gives the height.</summary>
    public bool FitWidth
    {
        get => _fitWidth;
        set
        {
            if (_fitWidth != value)
            {
                _fitWidth = value;
                ClampOffsets();
                StateHasChanged();
            }
        }
    }

    /// <summary>Draw scroll bars on the edges where the content does not fit. On by default.</summary>
    public bool ShowScrollBars
    {
        get => _showScrollBars;
        set
        {
            if (_showScrollBars != value)
            {
                _showScrollBars = value;
                ClampOffsets();
                StateHasChanged();
            }
        }
    }

    /// <summary>The column of the content at the left edge of the view.</summary>
    public long OffsetX => _offsetX;

    /// <summary>The row of the content at the top of the view.</summary>
    public long OffsetY => _offsetY;

    /// <summary>The cells of the view that show content: the view less the scroll bars that are showing.</summary>
    public Size Viewport => Measure().Viewport;

    /// <summary>The size of the content.</summary>
    public Size ExtentSize => Measure().Extent;

    /// <summary>Raised after the view has moved.</summary>
    public event EventHandler? Scrolled;

    /// <summary>Moves the view so that (<paramref name="x"/>, <paramref name="y"/>) of the content is at its top left, held inside the content.</summary>
    public void ScrollTo(long x, long y)
    {
        Measurement m = Measure();
        long newX = Math.Clamp(x, 0, Math.Max(0, m.Extent.Width - m.Viewport.Width));
        long newY = Math.Clamp(y, 0, Math.Max(0, m.Extent.Height - m.Viewport.Height));
        if (newX != _offsetX || newY != _offsetY)
        {
            _offsetX = newX;
            _offsetY = newY;
            Scrolled?.Invoke(this, EventArgs.Empty);
            StateHasChanged();
        }
    }

    public void ScrollBy(long columns, long rows) => ScrollTo(_offsetX + columns, _offsetY + rows);

    /// <summary>Scrolls just far enough to show the rectangle (content coordinates). One bigger than the view shows its top left.</summary>
    public void ScrollIntoView(long x, long y, long width, long height)
    {
        Measurement m = Measure();
        long newX = Bring(_offsetX, m.Viewport.Width, x, Math.Max(1, width));
        long newY = Bring(_offsetY, m.Viewport.Height, y, Math.Max(1, height));
        ScrollTo(newX, newY);
    }

    private static long Bring(long offset, long view, long start, long length)
    {
        if (start < offset)
        {
            return start;
        }

        if (start + length > offset + view)
        {
            return Math.Min(start, start + length - view);
        }

        return offset;
    }

    private readonly record struct Measurement(Size Extent, Size Viewport, bool VerticalBar, bool HorizontalBar);

    private Measurement Measure()
    {
        long width = ViewSize.Width;
        long height = ViewSize.Height;
        Size own = _content?.ViewSize ?? new Size(0, 0);
        long contentHeight = _contentSize?.Height ?? own.Height;
        long contentWidth = _fitWidth ? 0 : _contentSize?.Width ?? own.Width;
        bool vertical = false;
        bool horizontal = false;
        if (_showScrollBars)
        {
            // A bar takes a cell from the other direction, which can make that one need a bar too: settle in two passes.
            for (int pass = 0; pass < 2; pass++)
            {
                vertical = contentHeight > height - (horizontal ? 1 : 0);
                long viewWidth = width - (vertical ? 1 : 0);
                horizontal = !_fitWidth && contentWidth > viewWidth;
            }
        }

        long viewportWidth = Math.Max(0, width - (vertical ? 1 : 0));
        long viewportHeight = Math.Max(0, height - (horizontal ? 1 : 0));
        if (_fitWidth)
        {
            contentWidth = viewportWidth;
        }

        return new Measurement(
            new Size(Math.Min(contentWidth, 1000000), Math.Min(contentHeight, 1000000)),
            new Size(viewportWidth, viewportHeight),
            vertical,
            horizontal);
    }

    private void ClampOffsets()
    {
        Measurement m = Measure();
        _offsetX = Math.Clamp(_offsetX, 0, Math.Max(0, m.Extent.Width - m.Viewport.Width));
        _offsetY = Math.Clamp(_offsetY, 0, Math.Max(0, m.Extent.Height - m.Viewport.Height));
    }

    /// <summary>The control inside the content that holds the focus. <c>GetFocusedControl</c> answers with the content itself when nothing does, so that is not enough.</summary>
    private IControl? FocusedInContent()
    {
        IControl? focused = _content?.GetFocusedControl();
        return focused is { CanFocus: true, IsFocused: true } ? focused : null;
    }

    /// <summary>The view is a focus stop only when nothing inside can take the focus, so a static page still scrolls from the keyboard.</summary>
    private void UpdateFocusability()
    {
        bool shouldFocus = _content is null || (!_content.CanFocus && !_content.HasFocusableControl(out _));
        if (CanFocus != shouldFocus)
        {
            CanFocus = shouldFocus;
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        ClampOffsets();
    }

    #region Rendering
    public override bool Render(IBuffer buffer)
    {
        if (!IsVisible)
        {
            return true;
        }

        if (!HasStateChanged && !ContentHasChanged())
        {
            return true;
        }

        try
        {
            Draw(buffer);
            HasStateChanged = false;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine("Error rendering {Message}", exception.Message);
        }

        return false;
    }

    public override void GetControlsToRender(HashSet<IControl> controlsToRender, IEnumerable<int> layersToRefresh)
    {
        // The content is drawn by this control, never on its own: it would not be clipped.
        if (layersToRefresh.Contains(ZOrder) || ((HasStateChanged || ContentHasChanged()) && IsVisible))
        {
            controlsToRender.Add(this);
        }
    }

    private bool ContentHasChanged() => _content is not null && SubtreeChanged(_content);

    private static bool SubtreeChanged(IControl control)
    {
        if (control is Control { HasStateChanged: true, IsVisible: true })
        {
            return true;
        }

        foreach (IControl child in control.Controls.AsSnapshot())
        {
            if (child is not null && SubtreeChanged(child))
            {
                return true;
            }
        }

        return false;
    }

    private static void ClearChanged(IControl control)
    {
        if (control is Control c)
        {
            c.HasStateChanged = false;
        }

        foreach (IControl child in control.Controls.AsSnapshot())
        {
            if (child is not null)
            {
                ClearChanged(child);
            }
        }
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme scheme = ThemeService.CurrentColorScheme;
        iConsole.Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, scheme);
        UpdateFocusability();
        if (_content is null || !_content.IsVisible)
        {
            return;
        }

        Measurement m = Measure();
        _content.Position = new Position(0, 0);
        _content.ViewSize = m.Extent;
        _content.OnInitializeZOrder();
        FollowFocus(m);
        ClampOffsets();

        if (m.Extent.IsEmpty)
        {
            return;
        }

        if (_offscreen is null)
        {
            _offscreen = new OffscreenBuffer(AbsolutePosition, m.Extent);
        }
        else
        {
            _offscreen.Reset(AbsolutePosition, m.Extent);
        }

        _content.Draw(_offscreen);
        ClearChanged(_content);
        CopyViewport(buffer, m, scheme);
        if (m.VerticalBar)
        {
            DrawVerticalBar(buffer, m, scheme);
        }

        if (m.HorizontalBar)
        {
            DrawHorizontalBar(buffer, m, scheme);
        }
    }

    /// <summary>Scrolls to a control that has just taken the focus. Only a change of focus does, so the wheel is not undone by every repaint.</summary>
    private void FollowFocus(Measurement m)
    {
        IControl? focused = FocusedInContent();
        if (ReferenceEquals(focused, _lastFocused))
        {
            return;
        }

        _lastFocused = focused;
        if (focused is null || focused.ViewSize.IsEmpty)
        {
            return;
        }

        long x = focused.AbsolutePosition.X - AbsolutePosition.X;
        long y = focused.AbsolutePosition.Y - AbsolutePosition.Y;
        ScrollIntoView(x, y, focused.ViewSize.Width, focused.ViewSize.Height);
    }

    private void CopyViewport(IBuffer buffer, Measurement m, ColorScheme scheme)
    {
        if (_offscreen is null || m.Viewport.IsEmpty)
        {
            return;
        }

        Conxel blank = new(' ', scheme.Foreground, scheme.Background);
        int width = (int)m.Viewport.Width;
        for (int row = 0; row < m.Viewport.Height; row++)
        {
            ConxelString line = new();
            for (int column = 0; column < width; column++)
            {
                line.Append(_offscreen[(int)(_offsetX + column), (int)(_offsetY + row)] ?? blank);
            }

            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(width, 1), line);
        }

        Cursor cursor = _offscreen.Cursor;
        if (cursor.CursorPosition is { } spot)
        {
            long x = spot.X - AbsolutePosition.X - _offsetX;
            long y = spot.Y - AbsolutePosition.Y - _offsetY;
            if (x >= 0 && y >= 0 && x < m.Viewport.Width && y < m.Viewport.Height)
            {
                buffer.SetCursorPosition(new Position(AbsolutePosition.X + x, AbsolutePosition.Y + y), true);
            }
        }
    }

    /// <summary>The first and last cell of the thumb along a bar of <paramref name="length"/> cells.</summary>
    public static (long Start, long Size) Thumb(long length, long extent, long viewport, long offset)
    {
        if (length <= 0 || extent <= viewport)
        {
            return (0, Math.Max(0, length));
        }

        long size = Math.Clamp((long)Math.Round((double)length * viewport / extent), 1, length);
        long travel = length - size;
        long range = extent - viewport;
        long start = range <= 0 ? 0 : (long)Math.Round((double)travel * offset / range);
        return (Math.Clamp(start, 0, travel), size);
    }

    private void DrawVerticalBar(IBuffer buffer, Measurement m, ColorScheme scheme)
    {
        long length = m.Viewport.Height;
        (long start, long size) = Thumb(length, m.Extent.Height, m.Viewport.Height, _offsetY);
        for (long row = 0; row < length; row++)
        {
            bool thumb = row >= start && row < start + size;
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + m.Viewport.Width, AbsolutePosition.Y + row), new Size(1, 1),
                new ConxelString(thumb ? ThumbChar : TrackChar, scheme.Foreground, scheme.Background));
        }

        if (m.HorizontalBar)
        {
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + m.Viewport.Width, AbsolutePosition.Y + m.Viewport.Height), new Size(1, 1),
                new ConxelString(" ", scheme.Foreground, scheme.Background));
        }
    }

    private void DrawHorizontalBar(IBuffer buffer, Measurement m, ColorScheme scheme)
    {
        long length = m.Viewport.Width;
        (long start, long size) = Thumb(length, m.Extent.Width, m.Viewport.Width, _offsetX);
        ConxelString line = new();
        for (long column = 0; column < length; column++)
        {
            bool thumb = column >= start && column < start + size;
            line.Append(thumb ? ThumbChar : TrackChar, scheme.Foreground, scheme.Background);
        }

        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + m.Viewport.Height), new Size(length, 1), line);
    }
    #endregion

    #region Input
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (await base.HandleKeyEventAsync(keyEvent))
        {
            return true;
        }

        if (!IsEnabled || !IsVisible || (!IsFocused && FocusedInContent() is null))
        {
            return false;
        }

        Measurement m = Measure();
        long page = Math.Max(1, m.Viewport.Height - 1);
        long x = _offsetX;
        long y = _offsetY;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow: y--; break;
            case ConsoleKey.DownArrow: y++; break;
            case ConsoleKey.LeftArrow: x--; break;
            case ConsoleKey.RightArrow: x++; break;
            case ConsoleKey.PageUp: y -= page; break;
            case ConsoleKey.PageDown: y += page; break;
            case ConsoleKey.Home: y = 0; x = 0; break;
            case ConsoleKey.End: y = m.Extent.Height; break;
            default: return false;
        }

        if (keyEvent.Shift || keyEvent.Alt || keyEvent.Control)
        {
            return false;
        }

        long beforeX = _offsetX;
        long beforeY = _offsetY;
        ScrollTo(x, y);
        return _offsetX != beforeX || _offsetY != beforeY;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (!IsEnabled || !IsVisible || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return false;
        }

        Measurement m = Measure();
        long column = mouseEvent.Position.X - AbsolutePosition.X;
        long row = mouseEvent.Position.Y - AbsolutePosition.Y;
        bool inViewport = column < m.Viewport.Width && row < m.Viewport.Height;
        if (inViewport && _content is not null)
        {
            // The content thinks it sits at the view's corner, so the pointer is moved by how far the view has scrolled.
            MouseEvent shifted = mouseEvent with { Position = new Position(mouseEvent.Position.X + _offsetX, mouseEvent.Position.Y + _offsetY) };
            if (await _content.HandleMouseEventAsync(shifted))
            {
                return true;
            }
        }

        if (mouseEvent.WheelDelta != 0)
        {
            long before = _offsetY;
            ScrollBy(0, mouseEvent.WheelDelta > 0 ? -WheelStep : WheelStep);
            return _offsetY != before || m.Extent.Height > m.Viewport.Height;
        }

        if ((mouseEvent.LeftClicked || mouseEvent.IsDrag) && !mouseEvent.IsRelease)
        {
            if (m.VerticalBar && column == m.Viewport.Width && row < m.Viewport.Height)
            {
                ScrollTo(_offsetX, PositionOnTrack(row, m.Viewport.Height, m.Extent.Height - m.Viewport.Height));
                return true;
            }

            if (m.HorizontalBar && row == m.Viewport.Height && column < m.Viewport.Width)
            {
                ScrollTo(PositionOnTrack(column, m.Viewport.Width, m.Extent.Width - m.Viewport.Width), _offsetY);
                return true;
            }
        }

        return false;
    }

    /// <summary>The offset a click <paramref name="cell"/> cells along a bar <paramref name="length"/> cells long stands for.</summary>
    public static long PositionOnTrack(long cell, long length, long range)
    {
        if (length <= 1 || range <= 0)
        {
            return 0;
        }

        return Math.Clamp((long)Math.Round((double)cell / (length - 1) * range), 0, range);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        Measurement m = Measure();
        if (m.Extent.Height > m.Viewport.Height)
        {
            yield return ("Up/Down", "Scroll");
            yield return ("PgUp/PgDn", "Scroll a page");
            yield return ("Home/End", "Go to the top or bottom");
        }

        if (m.Extent.Width > m.Viewport.Width)
        {
            yield return ("Left/Right", "Scroll sideways");
        }
    }
    #endregion
}
