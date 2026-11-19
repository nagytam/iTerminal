using iConsole.Localization;
using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

public class SettingsQuotesTabPage : TabPage
{
    private ISettingsService _settingsService { get; set; }
    private CheckBox _checkBoxEnableQuotes { get; set; }
    private CheckBox _checkBoxEnableWeather { get; set; }
    private CheckBox _checkBoxEnableOnline { get; set; }

    private bool _isQuotesIndicatorEnabled = true;
    public bool IsQuotesIndicatorEnabled 
    {
        get
        {
            return _isQuotesIndicatorEnabled;
        }
        set
        {
            if (_isQuotesIndicatorEnabled != value)
            {
                _isQuotesIndicatorEnabled = value;
                _checkBoxEnableQuotes.IsChecked = _isQuotesIndicatorEnabled;
                _settingsService.Set(Settings.IsQuotesIndicatorEnabled, IsQuotesIndicatorEnabled.ToString());
            }
        }
    }

    private bool _isWeatherIndicatorEnabled = true;
    public bool IsWeatherIndicatorEnabled
    {
        get
        {
            return _isWeatherIndicatorEnabled;
        }
        set
        {
            if (_isWeatherIndicatorEnabled != value)
            {
                _isWeatherIndicatorEnabled = value;
                _checkBoxEnableWeather.IsChecked = _isWeatherIndicatorEnabled;
                _settingsService.Set(Settings.IsWeatherIndicatorEnabled, IsWeatherIndicatorEnabled.ToString());
            }
        }
    }

    private bool _isOnlineIndicatorEnabled = true;
    public bool IsOnlineIndicatorEnabled
    {
        get
        {
            return _isOnlineIndicatorEnabled;
        }
        set
        {
            if (_isOnlineIndicatorEnabled != value)
            {
                _isOnlineIndicatorEnabled = value;
                _checkBoxEnableOnline.IsChecked = _isOnlineIndicatorEnabled;
                _settingsService.Set(Settings.IsOnlineIndicatorEnabled, IsOnlineIndicatorEnabled.ToString());
            }
        }
    }

    public SettingsQuotesTabPage(
        ISettingsService settingsService,
        IThemeService themeService)
        : base(iConsole.Localization.UiText.Get("SettingsTab.Indicators", "3-Indicators "), themeService)
    {
        HotConsoleKey = ConsoleKey.D3;
        _settingsService = settingsService ?? NullSettingsService.Instance;

        _checkBoxEnableQuotes = new CheckBox(themeService)
        {
            Label = UiText.Get("Field.EnableQuotes", "Enable quotes"),
            IsChecked = _isQuotesIndicatorEnabled
        };
        _checkBoxEnableQuotes.CheckedChanged += OnCheckBoxEnableQuotesCheckedChanged;
        AddControl(_checkBoxEnableQuotes);

        _checkBoxEnableWeather = new CheckBox(themeService)
        {
            Label = UiText.Get("Field.EnableWeather", "Enable weather"),
            IsChecked = _isWeatherIndicatorEnabled
        };
        _checkBoxEnableWeather.CheckedChanged += OnCheckBoxEnableWeatherCheckedChanged;
        AddControl(_checkBoxEnableWeather);

        _checkBoxEnableOnline = new CheckBox(themeService)
        {
            Label = UiText.Get("Field.EnableOnline", "Enable online status"),
            IsChecked = _isOnlineIndicatorEnabled
        };
        _checkBoxEnableOnline.CheckedChanged += OnCheckBoxEnableOnlineCheckedChanged;
        AddControl(_checkBoxEnableOnline);
    }

    private void OnCheckBoxEnableQuotesCheckedChanged(object? sender, EventArgs e)
    {
        IsQuotesIndicatorEnabled = _checkBoxEnableQuotes.IsChecked;
    }

    private void OnCheckBoxEnableWeatherCheckedChanged(object? sender, EventArgs e)
    {
        IsWeatherIndicatorEnabled = _checkBoxEnableWeather.IsChecked;
    }

    private void OnCheckBoxEnableOnlineCheckedChanged(object? sender, EventArgs e)
    {
        IsOnlineIndicatorEnabled = _checkBoxEnableOnline.IsChecked;
    }

    public override Task OnInitializeAsync()
    {
        var isQuotesIndicatorEnabled = _settingsService.GetAsBool(Settings.IsQuotesIndicatorEnabled);
        if (isQuotesIndicatorEnabled is not null)
        {
            IsQuotesIndicatorEnabled = isQuotesIndicatorEnabled.Value;
        }

        var isWeatherIndicatorEnabled = _settingsService.GetAsBool(Settings.IsWeatherIndicatorEnabled);
        if (isWeatherIndicatorEnabled is not null)
        {
            IsWeatherIndicatorEnabled = isWeatherIndicatorEnabled.Value;
        }

        var isOnlineIndicatorEnabled = _settingsService.GetAsBool(Settings.IsOnlineIndicatorEnabled);
        if (isOnlineIndicatorEnabled is not null)
        {
            IsOnlineIndicatorEnabled = isOnlineIndicatorEnabled.Value;
        }
        return base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        _checkBoxEnableQuotes.Position = new iConsole.Data.Position(0, 0);
        _checkBoxEnableQuotes.ViewSize = new iConsole.Data.Size(ViewSize.Width, 1);

        _checkBoxEnableWeather.Position = new iConsole.Data.Position(0, 1);
        _checkBoxEnableWeather.ViewSize = new iConsole.Data.Size(ViewSize.Width, 1);

        _checkBoxEnableOnline.Position = new iConsole.Data.Position(0, 2);
        _checkBoxEnableOnline.ViewSize = new iConsole.Data.Size(ViewSize.Width, 1);
    }
}

