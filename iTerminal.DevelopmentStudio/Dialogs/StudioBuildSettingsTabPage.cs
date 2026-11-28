using iConsole.Controls;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The .NET SDK that build, run and test commands use, and the format of the SBOM. V and F in the studio change the same two
/// settings, and the studio reads them again when this page closes.
/// </summary>
public class StudioBuildSettingsTabPage : StudioSettingsTabPage
{
    private static readonly SbomFormat[] SbomFormats = [SbomFormat.Xml, SbomFormat.Json];

    private readonly DotnetSdk[] _sdks;
    private readonly ComboBox<DotnetSdk> _sdk;
    private readonly ComboBox<SbomFormat> _sbom;
    private readonly Label _hintLabel;

    public StudioBuildSettingsTabPage(ISettingsService settings, IReadOnlyList<DotnetSdk> sdks, IThemeService themeService)
        : base(UiText.Get("DevelopmentStudioSettings.TabBuild", "2-Build"), settings, themeService)
    {
        HotConsoleKey = ConsoleKey.D2;
        _sdks = sdks.ToArray();
        _sdk = new ComboBox<DotnetSdk>(themeService)
        {
            Label = UiText.Get("DevelopmentStudioSettings.Sdk", "SDK for build, run and test"),
            DisplaySelector = sdk => $"{sdk.Version}  {sdk.HostPath}",
        };
        _sdk.SetItems(_sdks);
        _sdk.SelectionChanged += OnChanged;

        _sbom = new ComboBox<SbomFormat>(themeService)
        {
            Label = UiText.Get("DevelopmentStudioSettings.SbomFormat", "SBOM format"),
            DisplaySelector = format => format == SbomFormat.Json ? "JSON" : "XML",
        };
        _sbom.SetItems(SbomFormats);
        _sbom.SelectionChanged += OnChanged;

        _hintLabel = new Label(UiText.Get("DevelopmentStudioSettings.BuildHint", "V switches the SDK and F the SBOM format; both changes are kept."), themeService);

        AddControl(_sdk);
        AddControl(_sbom);
        AddControl(_hintLabel);
    }

    /// <summary>The SDK new commands run with, or null when the studio found none.</summary>
    public DotnetSdk? Sdk
    {
        get => _sdks.Length == 0 || _sdk.SelectedIndex < 0 ? null : _sdks[_sdk.SelectedIndex];
        set
        {
            int index = value is null ? -1 : Array.FindIndex(_sdks, sdk => sdk.HostPath == value.HostPath && sdk.Version == value.Version);
            if (index >= 0) _sdk.SelectedIndex = index;
        }
    }

    /// <summary>The format the SBOM is written in.</summary>
    public SbomFormat Sbom
    {
        get => SbomFormats[Math.Max(0, _sbom.SelectedIndex)];
        set => _sbom.SelectedIndex = Array.IndexOf(SbomFormats, value);
    }

    protected override void OnLoad()
    {
        Sdk = StudioSettings.ReadSdk(Settings, _sdks) ?? DotnetSdk.Default(_sdks);
        Sbom = Parse(Settings.Get(StudioSettings.SbomFormatKey), SbomFormat.Xml);
    }

    protected override void Save()
    {
        if (Sdk is { } sdk) StudioSettings.WriteSdk(Settings, sdk);
        Settings.Set(StudioSettings.SbomFormatKey, Sbom.ToString());
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        Place(_sdk, 0, 1, ViewSize.Width);
        Place(_sbom, 0, 2, ViewSize.Width);
        Place(_hintLabel, 0, 4, ViewSize.Width);
    }
}
