using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

public class SettingsDialog : Dialog
{
    private Tab _tab { get; set; }

    public SettingsDialog(
        ISettingsService settingsService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        iConsole.Interfaces.IContentModelService? contentModelService = null,
        iTerminal.Core.Services.Music.IMusicPlayerService? musicPlayerService = null
        )
        : base(nameof(SettingsDialog), themeService)
    {
        Title = "Settings";
        _tab = new Tab(themeService) { Orientation = Orientation.Vertical };
        AddControl(_tab);
        var tabPageGlobal = new SettingsGlobalTabPage(themeService);
        _tab.AddTabPage(tabPageGlobal);
        var tabPageNetwork = new SettingsNetworkTabPage(themeService);
        _tab.AddTabPage(tabPageNetwork);
        var tabPageQuotes = new SettingsQuotesTabPage(settingsService, themeService);
        _tab.AddTabPage(tabPageQuotes);
        var tabPageMusic = new SettingsMusicTabPage(settingsService, beepService, dateTimeProviderService, themeService, musicPlayerService);
        _tab.AddTabPage(tabPageMusic);
        var tabPageDocuments = new SettingsDocumentsTabPage(settingsService, beepService, dateTimeProviderService, themeService);
        _tab.AddTabPage(tabPageDocuments);
        var tabPagePanels = new SettingsPanelsTabPage(settingsService, contentModelService, themeService);
        _tab.AddTabPage(tabPagePanels);
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        _tab.Position = new Position(1, 1);
        _tab.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 2);

        base.OnInitializeLayout();
    }
}

