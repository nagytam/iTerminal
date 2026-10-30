using iConsole.Buffers;
using iConsole.Data;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// Puts one control inside a panel that is bigger than it needs, at the start, the middle or the end across each axis: a dialog's
/// message in the middle of a page, a button at the bottom right. The content keeps the size it had when it was set (or the one
/// given), shrunk to fit; <see cref="StackAlignment.Stretch"/> on an axis fills it. Draws nothing of its own.
/// </summary>
public class AlignPanel : Control
{
    private IControl? _content;
    private StackAlignment _horizontal = StackAlignment.Center;
    private StackAlignment _vertical = StackAlignment.Center;
    private Size _contentSize = new(0, 0);

    public AlignPanel(IThemeService themeService) : base(themeService)
    {
        CanFocus = false;
        ViewSize = new Size(30, 10);
    }

    /// <summary>A panel that centres <paramref name="content"/> both ways.</summary>
    public static AlignPanel Centered(IThemeService themeService, IControl content, Size? size = null)
    {
        var panel = new AlignPanel(themeService);
        panel.SetContent(content, size);
        return panel;
    }

    public StackAlignment HorizontalAlignment
    {
        get => _horizontal;
        set
        {
            _horizontal = value;
            LayoutContent();
            StateHasChanged();
        }
    }

    public StackAlignment VerticalAlignment
    {
        get => _vertical;
        set
        {
            _vertical = value;
            LayoutContent();
            StateHasChanged();
        }
    }

    /// <summary>Hosts <paramref name="content"/> at <paramref name="size"/>, or the size it has now.</summary>
    public void SetContent(IControl content, Size? size = null)
    {
        if (_content is not null)
        {
            RemoveControl(_content);
        }

        _content = content;
        _contentSize = size ?? new Size(content.ViewSize.Width, content.ViewSize.Height);
        AddControl(content);
        LayoutContent();
        StateHasChanged();
    }

    /// <summary>Changes the size the content keeps.</summary>
    public void SetContentSize(Size size)
    {
        _contentSize = size;
        LayoutContent();
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutContent();
    }

    private static (long Offset, long Size) Place(StackAlignment alignment, long room, long wanted)
    {
        long size = alignment == StackAlignment.Stretch ? room : Math.Min(wanted, room);
        long offset = alignment switch
        {
            StackAlignment.Center => (room - size) / 2,
            StackAlignment.End => room - size,
            _ => 0,
        };
        return (offset, size);
    }

    private void LayoutContent()
    {
        if (_content is null)
        {
            return;
        }

        (long x, long width) = Place(_horizontal, ViewSize.Width, _contentSize.Width);
        (long y, long height) = Place(_vertical, ViewSize.Height, _contentSize.Height);
        _content.Position = new Position(x, y);
        _content.ViewSize = new Size(width, height);
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        iConsole.Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, ThemeService.CurrentColorScheme);
        LayoutContent();
        base.Draw(buffer);
    }
}

/// <summary>
/// Gives one control the room it has, held between a minimum and a maximum size on each axis (Cursive's <c>ResizedView</c>): a form
/// that should not grow wider than 60 cells however wide the screen is, or shrink below 30. Where the panel is smaller than the
/// minimum the content is cut off by the panel's edge; where it is bigger than the maximum the content sits at the start of the
/// room, or where <see cref="HorizontalAlignment"/> and <see cref="VerticalAlignment"/> put it.
/// </summary>
public class ConstrainedPanel : Control
{
    private IControl? _content;
    private StackAlignment _horizontal = StackAlignment.Start;
    private StackAlignment _vertical = StackAlignment.Start;

    public ConstrainedPanel(IThemeService themeService) : base(themeService)
    {
        CanFocus = false;
        ViewSize = new Size(30, 10);
    }

    public int MinWidth { get; set; }

    public int MaxWidth { get; set; } = int.MaxValue;

    public int MinHeight { get; set; }

    public int MaxHeight { get; set; } = int.MaxValue;

    public StackAlignment HorizontalAlignment
    {
        get => _horizontal;
        set
        {
            _horizontal = value;
            LayoutContent();
            StateHasChanged();
        }
    }

    public StackAlignment VerticalAlignment
    {
        get => _vertical;
        set
        {
            _vertical = value;
            LayoutContent();
            StateHasChanged();
        }
    }

    public void SetContent(IControl content)
    {
        if (_content is not null)
        {
            RemoveControl(_content);
        }

        _content = content;
        AddControl(content);
        LayoutContent();
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutContent();
    }

    /// <summary>The size the content gets in <paramref name="room"/> cells: the room, held between the limits (a minimum above the maximum wins).</summary>
    public static long Constrain(long room, int minimum, int maximum)
    {
        long size = Math.Min(room, maximum);
        return Math.Max(size, minimum);
    }

    private void LayoutContent()
    {
        if (_content is null)
        {
            return;
        }

        long width = Constrain(ViewSize.Width, MinWidth, MaxWidth);
        long height = Constrain(ViewSize.Height, MinHeight, MaxHeight);
        long x = _horizontal switch
        {
            StackAlignment.Center => (ViewSize.Width - width) / 2,
            StackAlignment.End => ViewSize.Width - width,
            _ => 0,
        };
        long y = _vertical switch
        {
            StackAlignment.Center => (ViewSize.Height - height) / 2,
            StackAlignment.End => ViewSize.Height - height,
            _ => 0,
        };
        _content.Position = new Position(Math.Max(0, x), Math.Max(0, y));
        _content.ViewSize = new Size(width, height);
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        iConsole.Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, ThemeService.CurrentColorScheme);
        LayoutContent();
        base.Draw(buffer);
    }
}
