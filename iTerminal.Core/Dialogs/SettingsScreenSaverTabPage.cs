using System.Globalization;
using iConsole.Localization;
using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using iTerminal.Core.ScreenSavers;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The screen saver: which one runs (or none), and how many minutes the terminal must be left alone before it starts.
/// Both are read live by the overlay, so a change applies to the next idle period without a restart.
/// </summary>
public class SettingsScreenSaverTabPage : TabPage
{
    private const int LabelWidth = 30;

    private readonly ISettingsService _settingsService;
    private readonly Label _effectLabel;
    private readonly ComboBox<ScreenSaverKind> _effect;
    private readonly Label _idleLabel;
    private readonly NumericUpDown _idleMinutes;
    private readonly Label _hintLabel;

    public SettingsScreenSaverTabPage(ISettingsService settingsService, IThemeService themeService)
        : base("8-Screen saver", themeService)
    {
        HotConsoleKey = ConsoleKey.D8;
        _settingsService = settingsService ?? NullSettingsService.Instance;

        _effectLabel = new Label(UiText.Get("ScreenSaver.Effect", "Effect"), themeService);
        AddControl(_effectLabel);
        _effect = new ComboBox<ScreenSaverKind>(themeService) { DisplaySelector = DisplayName };
        // Declared order: the index of a kind is its value.
        _effect.SetItems([ScreenSaverKind.Off, ScreenSaverKind.Starfield, ScreenSaverKind.MatrixRain, ScreenSaverKind.Clock]);
        _effect.SelectionChanged += (_, _) => OnEffectChanged();
        AddControl(_effect);

        _idleLabel = new Label(UiText.Get("ScreenSaver.Delay", "Start after (minutes)"), themeService);
        AddControl(_idleLabel);
        _idleMinutes = new NumericUpDown(themeService)
        {
            Minimum = ScreenSaverSettings.MinIdleMinutes,
            Maximum = ScreenSaverSettings.MaxIdleMinutes,
            Step = 1,
            LargeStep = 10,
        };
        _idleMinutes.ValueChanged += (_, _) => OnIdleMinutesChanged();
        AddControl(_idleMinutes);

        _hintLabel = new Label(
            UiText.Get("ScreenSaver.Hint", "A key press or a click wakes the screen. It does not start while a shell has the terminal."),
            themeService);
        AddControl(_hintLabel);
    }

    private static string DisplayName(ScreenSaverKind kind) => kind switch
    {
        ScreenSaverKind.Starfield => UiText.Get("ScreenSaver.Starfield", "Starfield"),
        ScreenSaverKind.MatrixRain => UiText.Get("ScreenSaver.MatrixRain", "Matrix rain"),
        ScreenSaverKind.Clock => UiText.Get("ScreenSaver.Clock", "Bouncing clock"),
        _ => UiText.Get("ScreenSaver.Off", "Off"),
    };

    public override Task OnInitializeAsync()
    {
        _effect.SelectedIndex = (int)ScreenSaverSettings.ParseKind(_settingsService.Get(Settings.ScreenSaverEffect));
        _idleMinutes.Value = ScreenSaverSettings.ParseIdleMinutes(_settingsService.Get(Settings.ScreenSaverIdleMinutes));
        return base.OnInitializeAsync();
    }

    private void OnEffectChanged()
    {
        // Selecting the stored value again is not a change; writing it would only rewrite the same text.
        string value = _effect.SelectedItem.ToString();
        if (!string.Equals(_settingsService.Get(Settings.ScreenSaverEffect), value, StringComparison.Ordinal))
        {
            _settingsService.Set(Settings.ScreenSaverEffect, value);
        }
    }

    private void OnIdleMinutesChanged()
    {
        string value = ((int)_idleMinutes.Value).ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(_settingsService.Get(Settings.ScreenSaverIdleMinutes), value, StringComparison.Ordinal))
        {
            _settingsService.Set(Settings.ScreenSaverIdleMinutes, value);
        }
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        _effectLabel.Position = new Position(0, 0);
        _effectLabel.ViewSize = new Size(LabelWidth, 1);
        _effect.Position = new Position(LabelWidth, 0);
        _effect.ViewSize = new Size(Math.Max(1, ViewSize.Width - LabelWidth), 1);

        _idleLabel.Position = new Position(0, 1);
        _idleLabel.ViewSize = new Size(LabelWidth, 1);
        _idleMinutes.Position = new Position(LabelWidth, 1);
        _idleMinutes.ViewSize = new Size(Math.Max(1, ViewSize.Width - LabelWidth), 1);

        _hintLabel.Position = new Position(0, 3);
        _hintLabel.ViewSize = new Size(ViewSize.Width, 1);
    }
}
