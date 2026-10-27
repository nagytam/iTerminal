using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// A container with the CSS box model: <see cref="Margin"/> (left untouched, so what is behind shows through),
/// a <see cref="BorderStyle"/> border one cell wide with an optional <see cref="Title"/>, and <see cref="Padding"/>
/// (cleared to the theme background). The hosted content fills what is left. Like <see cref="GroupBox"/> it is not
/// focusable; Tab moves to whatever it contains.
/// </summary>
public class BoxPanel : Control
{
    private Thickness _margin;
    private Thickness _padding;
    private BorderStyle _borderStyle = BorderStyle.Single;
    private string _title = string.Empty;
    private TextAlignment _titleAlignment = TextAlignment.Left;
    private IControl? _content;

    public BoxPanel(IThemeService themeService) : base(themeService)
    {
        CanFocus = false;
        ViewSize = new Size(30, 5);
    }

    /// <summary>Cells kept free outside the border.</summary>
    public Thickness Margin
    {
        get => _margin;
        set => Set(ref _margin, value);
    }

    /// <summary>Cells kept free between the border and the content.</summary>
    public Thickness Padding
    {
        get => _padding;
        set => Set(ref _padding, value);
    }

    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set => Set(ref _borderStyle, value);
    }

    /// <summary>Draws a drop shadow down the right and along the bottom of the box (see <see cref="Shadow"/>); the margin must leave room for it.</summary>
    public bool HasShadow { get; set; }

    /// <summary>Scrolls a title that is longer than the room in the top edge (see <see cref="Marquee"/>) instead of cutting it.</summary>
    public bool ScrollTitle { get; set; }

    /// <summary>Milliseconds on any fixed clock; replaceable so a test can move the title without waiting.</summary>
    public Func<long> TickSource
    {
        get => _tickSource;
        set
        {
            _tickSource = value ?? (() => Environment.TickCount64);
            // The scroll counts from the moment the clock is chosen.
            _titleStarted = _tickSource();
        }
    }

    private Func<long> _tickSource = () => Environment.TickCount64;
    private long _titleStarted = Environment.TickCount64;
    private int _titleRoom;
    private int _drawnTitleOffset = -1;

    /// <summary>Text set into the top edge. Ignored without a border.</summary>
    public string Title
    {
        get => _title;
        set => Set(ref _title, value ?? string.Empty);
    }

    public TextAlignment TitleAlignment
    {
        get => _titleAlignment;
        set => Set(ref _titleAlignment, value);
    }

    /// <summary>The border's width on each side: one cell, or none for <see cref="BorderStyle.None"/>.</summary>
    public Thickness BorderThickness => new(_borderStyle == BorderStyle.None ? 0 : 1);

    /// <summary>The border's rectangle, inside the margin, relative to the panel.</summary>
    public (Position Position, Size Size) BorderBounds =>
        (new Position(_margin.Left, _margin.Top), _margin.Deflate(ViewSize));

    /// <summary>Top-left of the area for content, inside margin, border and padding, relative to the panel.</summary>
    public Position ContentPosition
    {
        get
        {
            Thickness inset = _margin + BorderThickness + _padding;
            return new Position(inset.Left, inset.Top);
        }
    }

    /// <summary>Size of the area for content.</summary>
    public Size ContentSize => (_margin + BorderThickness + _padding).Deflate(ViewSize);

    /// <summary>Hosts a single control sized to fill the content area, replacing the previous one.</summary>
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

    public override void OnIdle()
    {
        base.OnIdle();
        if (ScrollTitle && _titleRoom > 0 && _title.Length > _titleRoom
            && Marquee.Offset(_title.Length, _titleRoom, TimeSpan.FromMilliseconds(TickSource() - _titleStarted)) != _drawnTitleOffset)
        {
            StateHasChanged();
        }
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        (Position origin, Size size) = BorderBounds;
        if (size.Width > 0 && size.Height > 0)
        {
            Position absolute = AbsolutePosition;
            Position boxPosition = new(absolute.X + origin.X, absolute.Y + origin.Y);
            if (HasShadow)
            {
                Shadow.Draw(buffer, ZOrder, boxPosition, size, colorScheme);
            }

            iConsole.Buffers.Buffer.Clear(buffer, ZOrder, boxPosition, size, colorScheme);
            if (_borderStyle != BorderStyle.None && size.Width >= 2 && size.Height >= 2)
            {
                DrawBox(buffer, boxPosition, size, colorScheme);
            }
        }

        LayoutContent();
        base.Draw(buffer);
    }

    private void DrawBox(IBuffer buffer, Position position, Size size, ColorScheme colorScheme)
    {
        GetColor(colorScheme, VisualState.Normal, out Color foreground, out Color background);
        BorderGlyphs glyphs = BorderGlyphs.For(_borderStyle);
        int width = (int)size.Width;
        int height = (int)size.Height;

        string top = glyphs.TopLeft + new string(glyphs.Horizontal, width - 2) + glyphs.TopRight;
        top = WithTitle(top, width);
        buffer.Write(ZOrder, position, new Size(width, 1), new ConxelString(top, foreground, background));

        string bottom = glyphs.BottomLeft + new string(glyphs.Horizontal, width - 2) + glyphs.BottomRight;
        buffer.Write(ZOrder, new Position(position.X, position.Y + height - 1), new Size(width, 1),
            new ConxelString(bottom, foreground, background));

        var side = new ConxelString(glyphs.Vertical.ToString(), foreground, background);
        for (int y = 1; y < height - 1; y++)
        {
            buffer.Write(ZOrder, new Position(position.X, position.Y + y), new Size(1, 1), side);
            buffer.Write(ZOrder, new Position(position.X + width - 1, position.Y + y), new Size(1, 1), side);
        }
    }

    /// <summary>Sets the title, with a space either side, into the top edge, clipped to the room between the corners.</summary>
    private string WithTitle(string edge, int width)
    {
        int room = width - 4;
        if (_title.Length == 0 || room <= 0)
        {
            return edge;
        }

        _titleRoom = room;
        TimeSpan elapsed = TimeSpan.FromMilliseconds(TickSource() - _titleStarted);
        string text = _title.Length <= room ? _title : ScrollTitle ? Marquee.Window(_title, room, elapsed) : _title[..room];
        _drawnTitleOffset = ScrollTitle ? Marquee.Offset(_title.Length, room, elapsed) : 0;
        string label = " " + text + " ";
        int start = _titleAlignment switch
        {
            TextAlignment.Center => 1 + ((width - 2 - label.Length) / 2),
            TextAlignment.Right => width - 1 - label.Length - 1,
            _ => 2,
        };
        start = Math.Clamp(start, 1, width - 1 - label.Length);
        return string.Concat(edge.AsSpan(0, start), label, edge.AsSpan(start + label.Length));
    }

    private void LayoutContent()
    {
        if (_content is null)
        {
            return;
        }

        _content.Position = ContentPosition;
        _content.ViewSize = ContentSize;
    }

    private void Set<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            LayoutContent();
            StateHasChanged();
        }
    }
}
