using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The Development studio's settings (Ctrl+,), in one tab per category: the tabs the studio opens on, the .NET SDK and the SBOM
/// format, the build configuration, the panel layout, and the analysis time estimates. Like the application's own settings there is no OK: each change is
/// saved as it is made, and the studio reads the choices again when this dialog closes (<see cref="Closed"/>). Esc closes it.
/// </summary>
public class DevelopmentStudioSettingsDialog : Dialog
{
    private readonly Tab _tab;
    private readonly Label _hintLabel;

    /// <param name="settings">The settings the studio keeps; each page reads and writes its own keys.</param>
    /// <param name="sdks">The .NET SDKs the studio found, newest first: the SDK page chooses one of them.</param>
    public DevelopmentStudioSettingsDialog(ISettingsService settings, IReadOnlyList<DotnetSdk> sdks, IThemeService themeService)
        : base(nameof(DevelopmentStudioSettingsDialog), themeService)
    {
        Title = UiText.Get("DevelopmentStudioSettings.Title", "Development studio settings");
        _tab = new Tab(themeService) { Orientation = Orientation.Vertical };
        AddControl(_tab);
        _hintLabel = new Label(UiText.Get("Settings.Hint", "Ctrl+Up/Down:page  Tab:next field  Esc:close"), themeService);
        AddControl(_hintLabel);
        _tab.AddTabPage(new StudioGeneralSettingsTabPage(settings, themeService));
        _tab.AddTabPage(new StudioBuildSettingsTabPage(settings, sdks, themeService));
        _tab.AddTabPage(new StudioConfigurationSettingsTabPage(settings, themeService));
        _tab.AddTabPage(new StudioLayoutSettingsTabPage(settings, themeService));
        _tab.AddTabPage(new StudioAnalysisSettingsTabPage(settings, themeService));
    }

    /// <summary>Called when the dialog closes, so the studio can read the choices it saved.</summary>
    public Action? Closed { get; set; }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        // The pages take every row but the bottom one, which holds the key hint.
        _tab.Position = new Position(1, 1);
        _tab.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 3);
        _hintLabel.Position = new Position(1, ViewSize.Height - 2);
        _hintLabel.ViewSize = new Size(ViewSize.Width - 2, 1);

        base.OnInitializeLayout();
    }

    public override void Close()
    {
        base.Close();
        Closed?.Invoke();
    }
}
