using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using iTerminal.Core.Services.BackgroundStart;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// When each background service starts: at start-up, after a delay, or only when the feature that uses it is opened.
/// The default is on demand, which is what keeps a scan or a server from running for a feature nobody opened.
///
/// <para>The choice is read when the application starts, so it takes effect the next time it starts. Each choice is
/// stored under <c>BackgroundStart.&lt;service&gt;</c>.</para>
/// </summary>
public class SettingsBackgroundTabPage : TabPage
{
    private const int LabelWidth = 46;

    private readonly ISettingsService _settingsService;
    private readonly List<(string Id, Label Label, ComboBox<BackgroundStartMode> Mode)> _rows = [];
    private readonly Label _hintLabel;

    public SettingsBackgroundTabPage(ISettingsService settingsService, IThemeService themeService)
        : base("7-Background", themeService)
    {
        HotConsoleKey = ConsoleKey.D7;
        _settingsService = settingsService ?? NullSettingsService.Instance;

        foreach ((string id, string text) in BackgroundServices.All)
        {
            Label label = new(text, themeService);
            AddControl(label);

            ComboBox<BackgroundStartMode> mode = new(themeService) { DisplaySelector = DisplayName };
            // Declared order: the index of a mode is its value.
            mode.SetItems([BackgroundStartMode.Immediately, BackgroundStartMode.Delayed, BackgroundStartMode.OnDemand]);
            string serviceId = id;
            mode.SelectionChanged += (_, _) => OnModeChanged(serviceId, mode);
            AddControl(mode);

            _rows.Add((id, label, mode));
        }

        _hintLabel = new Label(
            $"Applies the next time iTerminal starts. Delayed starts after {BackgroundStartService.DelayedStart.TotalSeconds:0} s, or sooner when the feature is opened.",
            themeService);
        AddControl(_hintLabel);
    }

    private static string DisplayName(BackgroundStartMode mode) => mode switch
    {
        BackgroundStartMode.Immediately => "Immediately",
        BackgroundStartMode.Delayed => $"Delayed ({BackgroundStartService.DelayedStart.TotalSeconds:0} s)",
        _ => "On demand",
    };

    public override Task OnInitializeAsync()
    {
        foreach ((string id, _, ComboBox<BackgroundStartMode> mode) in _rows)
        {
            mode.SelectedIndex = (int)BackgroundStartService.ParseMode(_settingsService.Get(BackgroundStartService.SettingKey(id)), id);
        }

        return base.OnInitializeAsync();
    }

    private void OnModeChanged(string serviceId, ComboBox<BackgroundStartMode> mode)
    {
        // Selecting the stored value again is not a change; writing it would only rewrite the same text.
        string value = mode.SelectedItem.ToString();
        if (string.Equals(_settingsService.Get(BackgroundStartService.SettingKey(serviceId)), value, StringComparison.Ordinal))
        {
            return;
        }

        _settingsService.Set(BackgroundStartService.SettingKey(serviceId), value);
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        int y = 0;
        foreach ((_, Label label, ComboBox<BackgroundStartMode> mode) in _rows)
        {
            label.Position = new Position(0, y);
            label.ViewSize = new Size(LabelWidth, 1);

            mode.Position = new Position(LabelWidth, y);
            mode.ViewSize = new Size(Math.Max(1, ViewSize.Width - LabelWidth), 1);
            y++;
        }

        y++;
        _hintLabel.Position = new Position(0, y);
        _hintLabel.ViewSize = new Size(ViewSize.Width, 1);
    }
}
