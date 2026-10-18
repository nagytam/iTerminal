using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A loading placeholder: grey bars where text will appear, with a lighter band sweeping across them, so a
/// panel that is waiting for data looks alive and keeps the shape of what is coming. The last line is
/// shorter, like the end of a paragraph. Remove it (or set <see cref="IsActive"/> to false, which blanks
/// it) when the data lands.
/// </summary>
public class Skeleton : Control
{
    private const char Bar = '░';
    private const char Shimmer = '▒';
    private const int ShimmerWidth = 4;

    private readonly IDateTimeProviderService _dateTimeProviderService;
    private DateTime _lastFrameChange;
    private int _offset;

    public Skeleton(IThemeService themeService, IDateTimeProviderService dateTimeProviderService)
        : base(themeService)
    {
        _dateTimeProviderService = dateTimeProviderService;
        _lastFrameChange = dateTimeProviderService.Now;
        ViewSize = new Size(30, 3);
    }

    public bool IsActive { get; set; } = true;

    /// <summary>How long the band rests on each cell; the idle loop runs far faster, so time gates the sweep.</summary>
    public TimeSpan FrameInterval { get; set; } = TimeSpan.FromMilliseconds(70);

    /// <summary>The band's left edge, which wraps around the widest line.</summary>
    public int Offset => _offset;

    /// <summary>Length of line <paramref name="line"/> of <paramref name="lines"/> on a row <paramref name="width"/> cells wide.</summary>
    public static int LineLength(int line, int lines, int width)
        => lines > 1 && line == lines - 1 ? Math.Max(1, width * 3 / 5) : width;

    /// <summary>One placeholder line with the band at <paramref name="offset"/> (it re-enters from the left after the end).</summary>
    public static string BuildLine(int length, int offset)
    {
        char[] cells = new char[length];
        Array.Fill(cells, Bar);
        for (int i = 0; i < ShimmerWidth; i++)
        {
            int at = (offset + i) % (length + ShimmerWidth);
            if (at < length)
            {
                cells[at] = Shimmer;
            }
        }

        return new string(cells);
    }

    public override void OnIdle()
    {
        if (IsActive)
        {
            DateTime now = _dateTimeProviderService.Now;
            if (now - _lastFrameChange >= FrameInterval)
            {
                _offset = (_offset + 1) % Math.Max(1, (int)ViewSize.Width + ShimmerWidth);
                _lastFrameChange = now;
                StateHasChanged();
            }
        }

        base.OnIdle();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        int lines = (int)ViewSize.Height;
        for (int y = 0; y < lines; y++)
        {
            string text = IsActive ? BuildLine(LineLength(y, lines, width), _offset) : string.Empty;
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + (uint)y), new Size(width, 1),
                new ConxelString(text.PadRight(width), colorScheme.DisabledForeground, colorScheme.Background));
        }
    }
}
