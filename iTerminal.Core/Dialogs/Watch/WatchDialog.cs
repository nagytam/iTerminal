using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model.Watch;
using iTerminal.Core.Services.Watch;

namespace iTerminal.Core.Dialogs.Watch;

/// <summary>
/// The clock tools in one dialog, as a horizontal <see cref="Tab"/>: the World clock (analog or digital), the Alarm, the
/// Stopwatch and the Timers. The pages keep their own keys; the dialog only makes Esc cancel an entry that is open before it
/// closes the dialog, and keeps the arrows with the page while text is being typed.
/// </summary>
public class WatchDialog : Dialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 26;

    private readonly Tab _tab;
    private readonly WatchTabPage[] _pages;

    public WatchDialog(
        IThemeService themeService,
        IDateTimeProviderService dateTimeProviderService,
        WatchStore store,
        WatchTimers timers,
        WatchStopwatch stopwatch)
        : base(nameof(WatchDialog), themeService)
    {
        Title = UiText.Get("Dialog.Watch", "Watch");

        _tab = new Tab(themeService) { Orientation = Orientation.Horizontal };
        AddControl(_tab);

        _pages =
        [
            new WatchWorldClockTabPage(store, dateTimeProviderService, themeService),
            new WatchAlarmTabPage(store, dateTimeProviderService, themeService),
            new WatchStopwatchTabPage(stopwatch, dateTimeProviderService, themeService),
            new WatchTimersTabPage(timers, dateTimeProviderService, themeService),
        ];
        foreach (var page in _pages)
        {
            _tab.AddTabPage(page);
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(DialogWidth, DialogHeight));
        }

        // The tab strip is part of the tab, so it takes the whole inside of the frame.
        var innerWidth = Math.Max(0, (int)ViewSize.Width - 2);
        var innerHeight = Math.Max(0, (int)ViewSize.Height - 2);
        if (innerWidth > 0 && innerHeight > 0)
        {
            _tab.Position = new Position(1, 1);
            _tab.ViewSize = new Size(innerWidth, innerHeight);
        }

        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_tab.FocusedPage is WatchTabPage page)
        {
            foreach (var hint in page.GetKeyHints())
            {
                yield return hint;
            }
        }

        yield return ("Left/Right", UiText.Get("DialogHint.ChangeTab", "Change tab"));
    }

    public override async Task ShowAsync()
    {
        // The dialog is built once and opened many times: an entry or a question left open last time is dropped.
        foreach (var page in _pages)
        {
            page.CancelPendingInput();
        }

        await base.ShowAsync();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        if (_tab.FocusedPage is WatchTabPage { HasPendingInput: true } page)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.Escape && plain)
            {
                page.CancelPendingInput();
                StateHasChanged();
                return true;
            }

            // The arrows belong to the text being typed, so the tab does not change under it.
            if (keyEvent.ConsoleKey is ConsoleKey.LeftArrow or ConsoleKey.RightArrow && plain)
            {
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }
}
