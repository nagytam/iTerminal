using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Ctrl+F1 — "where do you want to go?". Seven pages, one per way of naming a folder, and the same
/// three keys on every one of them: the arrows move, Enter goes, Esc cancels. Ctrl+Up/Ctrl+Down (or
/// Ctrl and the number in a page's name) changes page; the arrows alone are left to the page's own
/// list, which is the only thing that makes the lists usable.
///
/// <b>Esc closes it at any time</b>, from any page, whatever is focused — and where the page has a
/// sub-state on screen, Esc peels that off first: one press takes down a suggestion list on the Path
/// or SSH page, the next closes the selector. That is inherited rather than implemented here
/// (<see cref="Dialog.HandleKeyEventAsync"/> claims Esc ahead of the children and dismisses an open
/// <c>ITransientOverlayHost</c> before it closes), but it is the promise the dialog makes on its own
/// border, so it is pinned down by <c>FolderSelectorDialogTest</c> rather than left to the base class.
///
/// <b>Selecting a page puts the cursor on that page's first control</b>, which is what makes the
/// arrows work the moment a page comes up and is why every page lists its list or its text box before
/// its buttons — <c>Tab.SwitchToPage</c> focuses the page, and a page cannot hold focus itself.
/// </summary>
public class FolderSelectorDialog : Dialog
{
    private Tab _tab { get; set; }

    /// <summary>The pages, in tab order. Exposed so a test can drive a page without reaching
    /// through <see cref="Tab.TabPages"/> and casting.</summary>
    public IReadOnlyList<FolderSelectorTabPage> TabPages { get; }

    public FolderSelectorDialog(
        IContentModelService contentModelService,
        FolderHistoryService folderHistoryService,
        DriveInfoModel driveInfoModel,
        FolderIndexModel folderIndexModel,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        ISettingsService settingsService,
        IFrequentExecutionGuardService frequentExecutionGuardService,
        IThemeService themeService,
        HelpDialog? helpDialog = null)
        : base(nameof(FolderSelectorDialog), themeService)
    {
        Title = iConsole.Localization.UiText.Get("Dialog.FolderSelector", "Folder Selector");

        _tab = new Tab(themeService) { Orientation = Orientation.Vertical };
        AddControl(_tab);

        TabPages =
        [
            new FolderSelectorFavouritesTabPage(contentModelService, settingsService, themeService),
            new FolderSelectorDrivesTabPage(driveInfoModel, contentModelService, themeService),
            new FolderSelectorHistoryTabPage(folderHistoryService, contentModelService, themeService),
            new FolderSelectorSpecialFoldersTabPage(contentModelService, themeService),
            new FolderSelectorFolderTreeTabPage(contentModelService, folderIndexModel, frequentExecutionGuardService, themeService),
            new FolderSelectorRemoteShellTabPage(contentModelService, beepService, dateTimeProviderService, settingsService, frequentExecutionGuardService, themeService),
            new FolderSelectorPathTabPage(contentModelService, folderHistoryService, beepService, dateTimeProviderService, settingsService, themeService),
        ];

        foreach (var tabPage in TabPages)
        {
            _tab.AddTabPage(tabPage);
        }

        // F1 works inside any modal control through KeyboardEventConsumerJob's fallback, but only
        // when nothing swallowed the key first. Binding it here means the dialog advertises its own
        // help rather than depending on that, and lets a test press F1 without a Desktop.
        if (helpDialog is not null)
        {
            AddCommand(new HelpDialogCommand(helpDialog));
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        _tab.Position = new Position(1, 1);
        _tab.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), Math.Max(0, ViewSize.Height - 2));

        base.OnInitializeLayout();
    }

    /// <summary>
    /// The keys that apply on every page, on the bottom border. The pages themselves have no room
    /// for it — each one is a full-height list — and a dialog whose whole job is navigation should
    /// not need F1 pressed before it says how to navigate.
    /// </summary>
    public static string GetFooter() => GetFooter(int.MaxValue);

    /// <summary>
    /// The footer for a border <paramref name="maxWidth"/> wide. Hints that do not fit are left out whole,
    /// paging first, then moving — shrinking the line cut the last words of a hint mid-word ("C...page").
    /// Go, help and cancel stay.
    /// </summary>
    public static string GetFooter(int maxWidth)
    {
        string[] hints = ["Enter go", "Up/Down move", "Ctrl+Up/Down page", "F1 help", "Esc cancel"];
        List<string> kept = [.. hints];
        string Join() => " " + string.Join(" " + Border.Vertical + " ", kept) + " ";
        foreach (string drop in new[] { "Ctrl+Up/Down page", "Up/Down move" })
        {
            if (Join().Length <= maxWidth)
            {
                break;
            }
            kept.Remove(drop);
        }

        return Join();
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);

        // Drawn over the border base.DrawChrome just laid down, because PopUpWindow always passes an
        // empty footer. StringHelper.AlignCenter drops a footer longer than the line whole — the
        // border would silently lose its key hints — so it is shrunk to fit first.
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var footer = GetFooter(Math.Max(0, (int)ViewSize.Width - 4)).Shrink(Math.Max(0, (int)ViewSize.Width - 4));
        if (string.IsNullOrEmpty(footer))
        {
            return;
        }
        var line = Border.BottomLeft + Border.Horizontal.Repeat((int)ViewSize.Width - 2) + Border.BottomRight;
        line = StringHelper.AlignCenter(line, footer);
        var lineString = new ConxelString(line, foreground, colorScheme.Background);
        buffer.Write(ZOrder,
            new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1),
            new Size(lineString.Count, 1),
            lineString);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", "Go to the folder the focused page has under its cursor, and close.");
        yield return ("Esc", "Dismiss the suggestion list if one is up, otherwise close without navigating.");
        yield return ("Ctrl+Up / Ctrl+Down", "Move to the previous / next page of the selector.");
        yield return ("Ctrl+0 … Ctrl+6", "Jump straight to a page by the number in its name, cursor on its first control.");
        yield return ("Tab / Shift+Tab", "Move between the focused page's list and its buttons.");
    }
}
