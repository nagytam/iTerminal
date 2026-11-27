using iConsole.Controls;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.DevelopmentStudio.Services;
using iTerminal.Core.Services;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The tabs the studio opens on: the explorer's and the output panel's. F6 and F7 change them too, and the studio keeps the last one
/// shown, so this page and the keys are the same setting.
/// </summary>
public class StudioGeneralSettingsTabPage : StudioSettingsTabPage
{
    private static readonly DevelopmentStudioDialog.ExplorerMode[] ExplorerModes = Enum.GetValues<DevelopmentStudioDialog.ExplorerMode>();
    private static readonly DevelopmentStudioDialog.OutputTab[] OutputTabs = Enum.GetValues<DevelopmentStudioDialog.OutputTab>();

    private readonly ComboBox<DevelopmentStudioDialog.ExplorerMode> _explorer;
    private readonly ComboBox<DevelopmentStudioDialog.OutputTab> _output;
    private readonly Label _hintLabel;

    public StudioGeneralSettingsTabPage(ISettingsService settings, IThemeService themeService)
        : base(UiText.Get("DevelopmentStudioSettings.TabGeneral", "1-General"), settings, themeService)
    {
        HotConsoleKey = ConsoleKey.D1;
        _explorer = new ComboBox<DevelopmentStudioDialog.ExplorerMode>(themeService)
        {
            Label = UiText.Get("DevelopmentStudioSettings.ExplorerAtStart", "Explorer tab at start"),
            DisplaySelector = DevelopmentStudioDialog.ExplorerModeHeading,
        };
        _explorer.SetItems(ExplorerModes);
        _explorer.SelectionChanged += OnChanged;

        _output = new ComboBox<DevelopmentStudioDialog.OutputTab>(themeService)
        {
            Label = UiText.Get("DevelopmentStudioSettings.OutputAtStart", "Output tab at start"),
            DisplaySelector = DevelopmentStudioDialog.OutputTabHeading,
        };
        _output.SetItems(OutputTabs);
        _output.SelectionChanged += OnChanged;

        _hintLabel = new Label(UiText.Get("DevelopmentStudioSettings.GeneralHint", "F6 and F7 change these too: the studio opens on the tab it was last left on."), themeService);

        AddControl(_explorer);
        AddControl(_output);
        AddControl(_hintLabel);
    }

    /// <summary>The explorer mode the studio opens on.</summary>
    public DevelopmentStudioDialog.ExplorerMode Explorer
    {
        get => ExplorerModes[Math.Max(0, _explorer.SelectedIndex)];
        set => _explorer.SelectedIndex = Array.IndexOf(ExplorerModes, value);
    }

    /// <summary>The output tab the studio opens on.</summary>
    public DevelopmentStudioDialog.OutputTab Output
    {
        get => OutputTabs[Math.Max(0, _output.SelectedIndex)];
        set => _output.SelectedIndex = Array.IndexOf(OutputTabs, value);
    }

    protected override void OnLoad()
    {
        Explorer = Parse(Settings.Get(StudioSettings.ExplorerModeKey), DevelopmentStudioDialog.ExplorerMode.Solution);
        Output = Parse(Settings.Get(StudioSettings.OutputTabKey), DevelopmentStudioDialog.OutputTab.Output);
    }

    protected override void Save()
    {
        Settings.Set(StudioSettings.ExplorerModeKey, Explorer.ToString());
        Settings.Set(StudioSettings.OutputTabKey, Output.ToString());
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        Place(_explorer, 0, 1, ViewSize.Width);
        Place(_output, 0, 2, ViewSize.Width);
        Place(_hintLabel, 0, 4, ViewSize.Width);
    }
}
