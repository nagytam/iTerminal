using System.Globalization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model.Watch;
using iTerminal.Core.Services.Watch;

namespace iTerminal.Core.Dialogs.Watch;

/// <summary>
/// The time in the computer's own zone and in the zones the person added, in one list. The zone under the cursor is shown
/// large: as a digital readout, or on an analog face (V switches between the two). N adds a zone by its system id
/// (<c>Europe/Budapest</c>), Delete removes one; the computer's own zone is always first and stays.
/// </summary>
public sealed class WatchWorldClockTabPage : WatchListTabPage
{
    private const int AnalogWidth = 30;
    private const int AnalogMaxRows = 13;
    private const int AnalogMinRows = 5;
    private const int DigitalRows = 5;
    private const int NameWidth = 28;

    private readonly WatchStore _store;
    private readonly IDateTimeProviderService _dateTimeProviderService;
    private bool _showAnalog;
    private int _faceRows = DigitalRows + 1;

    public WatchWorldClockTabPage(WatchStore store, IDateTimeProviderService dateTimeProviderService, IThemeService themeService)
        : base(UiText.Get("Watch.TabWorldClock", "World clock"), themeService)
    {
        _store = store;
        _dateTimeProviderService = dateTimeProviderService;
    }

    protected override int ItemCount => 1 + _store.WorldClocks.Count;

    protected override int HeadingRow => _faceRows;

    protected override string HeadingText
    {
        get
        {
            var index = Math.Max(0, Cursor);
            var (_, zone) = ZoneAt(index);
            var now = WatchTimeZones.ConvertFromUtc(_dateTimeProviderService.UtcNow, zone);
            return $" {now.ToString("D", LanguageCulture)}  ·  {WatchTimeZones.OffsetLabel(_dateTimeProviderService.UtcNow, zone)}";
        }
    }

    protected override string EmptyText => string.Empty;

    protected override string EntryPrompt => UiText.Get("Watch.NewZonePrompt", "Add a time zone, e.g. Europe/Budapest");

    protected override bool CanDelete(int index) => index > 0;

    protected override string RowText(int index)
    {
        var (name, zone) = ZoneAt(index);
        var utcNow = _dateTimeProviderService.UtcNow;
        var time = WatchTimeZones.ConvertFromUtc(utcNow, zone);
        return $"{name.Cut(NameWidth).ExpandRight(NameWidth)} {WatchTimeZones.FormatTime(time)}  {WatchTimeZones.OffsetLabel(utcNow, zone)}";
    }

    protected override void PaintAboveList(IBuffer buffer)
    {
        var (name, zone) = ZoneAt(Math.Max(0, Cursor));
        var time = WatchTimeZones.ConvertFromUtc(_dateTimeProviderService.UtcNow, zone);
        var (foreground, background) = RowColours(highlighted: false);
        WriteRow(buffer, 0, $" {name}", foreground, background);

        int width = (int)ViewSize.Width;
        if (!_showAnalog)
        {
            var digits = Digits.Render(WatchTimeZones.FormatTime(time), DigitsStyle.Large);
            int offset = Math.Max(0, (width - Digits.Measure(WatchTimeZones.FormatTime(time), DigitsStyle.Large)) / 2);
            for (int line = 0; line < DigitalRows; line++)
            {
                string text = line < digits.Count ? new string(' ', offset) + digits[line] : string.Empty;
                WriteRow(buffer, 1 + line, text, foreground, background);
            }

            return;
        }

        var face = AnalogClock.Render(Math.Min(AnalogWidth, width), _faceRows - 1, time.TimeOfDay, showSeconds: true);
        int left = Math.Max(0, (width - AnalogWidth) / 2);
        for (int line = 0; line < _faceRows - 1; line++)
        {
            string text = line < face.Count ? new string(' ', left) + face[line] : string.Empty;
            WriteRow(buffer, 1 + line, text, foreground, background);
        }
    }

    protected override void LayoutPage(Size size)
    {
        // The face takes what the list does not need: a short page gets a smaller dial rather than a list with no room.
        _faceRows = _showAnalog ? Math.Clamp((int)size.Height - 9, AnalogMinRows, AnalogMaxRows) + 1 : DigitalRows + 1;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Up/Down", UiText.Get("DialogHint.MoveZone", "Choose a zone"));
        yield return ("V", UiText.Get("Watch.ToggleFace", "Analog or digital"));
        yield return ("N/Insert", UiText.Get("Watch.AddZone", "Add a zone"));
        yield return ("Delete", UiText.Get("Watch.RemoveZone", "Remove the zone (asks first)"));
    }

    protected override string? AddItem(string text)
    {
        if (text.Length == 0)
        {
            return UiText.Get("Watch.TypeZone", "Type a time zone id, e.g. Europe/Budapest");
        }

        if (!WatchTimeZones.TryFind(text, out var zone))
        {
            return string.Format(CultureInfo.CurrentCulture, UiText.Get("Watch.BadZone", "\"{0}\" is not a time zone, e.g. Europe/Budapest"), text);
        }

        return _store.AddWorldClock(zone.Id) ? null : UiText.Get("Watch.ZoneListed", "That zone is already listed");
    }

    protected override void DeleteItem(int index)
    {
        if (index > 0)
        {
            _store.RemoveWorldClock(_store.WorldClocks[index - 1]);
        }
    }

    protected override Task<bool> HandleOtherKeyAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        if (keyEvent.ConsoleKey == ConsoleKey.V && plain)
        {
            _showAnalog = !_showAnalog;
            LayoutPage(ViewSize);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <summary>The name and zone of the list row: the computer's own, or the stored zone (which is kept as the computer's zone if the system no longer has it).</summary>
    private (string Name, TimeZoneInfo Zone) ZoneAt(int index)
    {
        if (index <= 0)
        {
            return (WatchTimeZones.LocalLabel, TimeZoneInfo.Local);
        }

        var id = _store.WorldClocks.ElementAtOrDefault(index - 1) ?? string.Empty;
        return WatchTimeZones.TryFind(id, out var zone) ? (zone.Id, zone) : (id, TimeZoneInfo.Utc);
    }

    /// <summary>The culture the day's name is written in: the language the person chose, not the one the process runs in.</summary>
    private static CultureInfo LanguageCulture => UiText.Current?.Culture ?? CultureInfo.CurrentCulture;
}
