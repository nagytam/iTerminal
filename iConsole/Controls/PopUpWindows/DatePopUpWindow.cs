using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Services;

namespace iConsole.Controls.PopUpWindows;

/// <summary>
/// The status bar's date pop-up (F1): the local and UTC date and time, the time zone with its UTC offset, and a
/// schematic world map with a marker on the zone's meridian. Every label is translated through <see cref="UiText"/>.
/// </summary>
public class DatePopUpWindow : PopUpWindow
{
    private const string TitleKey = "Dialog.DateTimeTimeZone";
    private const string TitleFallback = "Date, Time and TimeZone";

    private const int MapWidth = 72;
    private const int MapHeight = 12;

    // The system does not give a latitude for its zone, so the marker is drawn on the 45th parallel: only the longitude is real.
    private const double MarkerLatitude = 45;

    private static readonly Size WindowSize = new(80, 22);

    private readonly IThemeService _themeService;
    private Func<TimeZoneInfo> _localTimeZone = () => TimeZoneInfo.Local;

    public DatePopUpWindow(IThemeService themeService)
        : base(UiText.Get(TitleKey, TitleFallback), themeService)
    {
        _themeService = themeService;
        Hide();
    }

    /// <summary>The zone the local lines, the offset and the map describe. A test sets a fixed one; it defaults to the system's zone.</summary>
    public Func<TimeZoneInfo> LocalTimeZone
    {
        get => _localTimeZone;
        set => _localTimeZone = value ?? (() => TimeZoneInfo.Local);
    }

    /// <summary>
    /// The localized title, read again on every draw so a language change shows in it. The title is fixed: a value set on it
    /// is not kept, because the base constructor sets it before the language is known.
    /// </summary>
    public override string Title
    {
        get => UiText.Get(TitleKey, TitleFallback);
        set { }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        CenterInParent(WindowSize);
    }

    public override void OnIdle()
    {
        if (IsVisible)
        {
            StateHasChanged();
        }
        base.OnIdle();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var colorScheme = _themeService.CurrentColorScheme;

        int innerWidth = (int)ViewSize.Width - 2;
        int innerHeight = (int)ViewSize.Height - 2;
        if (innerWidth <= 0 || innerHeight <= 0)
        {
            // A popup smaller than its border has no room for text; drawing nothing is what the layout asks for.
            return;
        }

        // The content is the six text rows, a blank row, the map and a blank row. A popup shorter than that shows the top of it.
        TimeZoneInfo zone = LocalTimeZone();
        DateTime utcNow = DateTime.UtcNow;
        DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
        TimeSpan offset = zone.GetUtcOffset(utcNow);
        bool summerTime = zone.IsDaylightSavingTime(utcNow);
        string dstState = UiText.Get(
            summerTime ? "Dialog.DateTimeTimeZone.SummerTime" : "Dialog.DateTimeTimeZone.StandardTime",
            summerTime ? "summer time" : "standard time");

        var content = new List<string>
        {
            Label("Date", "Date:") + " " + localNow.ToString("yyyy-MM-dd"),
            Label("Time", "Time:") + " " + localNow.ToString("HH:mm:ss"),
            Label("TimeZone", "Time zone:") + " " + zone.DisplayName,
            string.Format(UiText.Get(TitleKey + ".UtcOffset", "UTC offset: {0} ({1})"), FormatOffset(offset), dstState),
            Label("DateUtc", "Date (UTC):") + " " + utcNow.ToString("yyyy-MM-dd"),
            Label("TimeUtc", "Time (UTC):") + " " + utcNow.ToString("HH:mm:ss"),
            string.Empty,
        };

        string[] map = SchematicWorldMap.Render(MapWidth, MapHeight, zone.BaseUtcOffset.TotalHours * 15, MarkerLatitude);
        int mapLeft = Math.Max(0, (innerWidth - MapWidth) / 2);
        foreach (string mapRow in map)
        {
            content.Add(new string(' ', mapLeft) + mapRow);
        }

        content.Add(string.Empty);

        for (int row = 0; row < innerHeight; row++)
        {
            string text = row < content.Count ? content[row] : string.Empty;
            // Padded to the interior: the drawing starts one column in, so the right border is left alone.
            var lineString = new ConxelString(Fit(text, innerWidth), colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder,
                new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 1 + (uint)row), ViewSize, lineString);
        }
    }

    // A line is cut to the interior, so a long zone name or a narrow popup never writes past the border.
    private static string Fit(string text, int width) =>
        text.Length > width ? text[..width] : text.PadRight(width);

    private static string Label(string name, string english) =>
        UiText.Get(TitleKey + "." + name, english);

    private static string FormatOffset(TimeSpan offset)
    {
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        TimeSpan magnitude = offset.Duration();
        return $"{sign}{(int)magnitude.TotalHours:00}:{magnitude.Minutes:00}";
    }
}
