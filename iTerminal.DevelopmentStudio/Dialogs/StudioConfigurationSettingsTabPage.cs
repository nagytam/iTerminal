using iConsole.Controls;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The build configuration of the .NET commands: Debug or Release. D in the studio switches the same setting, and the studio reads it
/// again when this page's dialog closes. Release builds optimised and does not start the debugger.
/// </summary>
public class StudioConfigurationSettingsTabPage : StudioSettingsTabPage
{
    private static readonly BuildConfiguration[] Configurations = Enum.GetValues<BuildConfiguration>();

    private readonly ComboBox<BuildConfiguration> _configuration;
    private readonly Label _hintLabel;

    public StudioConfigurationSettingsTabPage(ISettingsService settings, IThemeService themeService)
        : base(UiText.Get("DevelopmentStudioSettings.TabConfiguration", "3-Configuration"), settings, themeService)
    {
        HotConsoleKey = ConsoleKey.D3;
        _configuration = new ComboBox<BuildConfiguration>(themeService)
        {
            Label = UiText.Get("DevelopmentStudioSettings.Configuration", "Build configuration"),
            DisplaySelector = configuration => configuration.ToString(),
        };
        _configuration.SetItems(Configurations);
        _configuration.SelectionChanged += OnChanged;

        _hintLabel = new Label(UiText.Get("DevelopmentStudioSettings.ConfigurationHint", "D switches Debug and Release; Release builds optimised and does not start the debugger."), themeService);

        AddControl(_configuration);
        AddControl(_hintLabel);
    }

    /// <summary>The configuration the .NET commands use.</summary>
    public BuildConfiguration Configuration
    {
        get => Configurations[Math.Max(0, _configuration.SelectedIndex)];
        set => _configuration.SelectedIndex = Array.IndexOf(Configurations, value);
    }

    protected override void OnLoad()
    {
        Configuration = Parse(Settings.Get(StudioSettings.BuildConfigurationKey), BuildConfiguration.Debug);
    }

    protected override void Save()
    {
        Settings.Set(StudioSettings.BuildConfigurationKey, Configuration.ToString());
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        Place(_configuration, 0, 1, ViewSize.Width);
        Place(_hintLabel, 0, 3, ViewSize.Width);
    }
}
