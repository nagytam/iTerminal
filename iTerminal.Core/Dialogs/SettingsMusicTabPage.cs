using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The music folder: the root the library indexer walks and the music player searches. Leaving it
/// empty falls back to the platform's own Music folder, which the hint line spells out so an empty
/// box never looks like "nothing is indexed".
/// </summary>
public class SettingsMusicTabPage : TabPage
{
    private readonly ISettingsService _settingsService;
    private readonly TextBox _musicFolderTextBox;
    private readonly Label _musicFolderLabel;
    private readonly Label _hintLabel;
    private readonly IMusicPlayerService? _musicPlayerService;
    private readonly Label _volumeLabel;
    private readonly Slider _volumeSlider;

    public SettingsMusicTabPage(
        ISettingsService settingsService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        IMusicPlayerService? musicPlayerService = null)
        : base("4-Music ", themeService)
    {
        _musicPlayerService = musicPlayerService;
        HotConsoleKey = ConsoleKey.D4;
        _settingsService = settingsService ?? NullSettingsService.Instance;

        _musicFolderLabel = new Label("Music folder:", themeService);
        AddControl(_musicFolderLabel);

        _musicFolderTextBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            MaxLength = 4096,
        };
        _musicFolderTextBox.PropertyChanged += OnMusicFolderChanged;
        AddControl(_musicFolderTextBox);

        _hintLabel = new Label(string.Empty, themeService);
        AddControl(_hintLabel);

        // The playback volume, the same 0-100 the + and - keys move in steps of 5. The player persists it
        // (MusicSessionJob), so the slider only has to set it; without a player there is nothing to set.
        _volumeLabel = new Label("Volume:", themeService) { IsVisible = _musicPlayerService is not null };
        _volumeSlider = new Slider(themeService)
        {
            Minimum = 0,
            Maximum = 100,
            Step = 5,
            LargeStep = 20,
            ShowValue = true,
            IsVisible = _musicPlayerService is not null,
        };
        _volumeSlider.ValueChanged += OnVolumeChanged;
        AddControl(_volumeLabel);
        AddControl(_volumeSlider);
    }

    /// <summary>The volume the slider shows, 0-100.</summary>
    public int Volume
    {
        get => (int)_volumeSlider.Value;
        set => _volumeSlider.Value = value;
    }

    private void OnVolumeChanged(object? sender, EventArgs e)
    {
        if (_musicPlayerService is not null && _musicPlayerService.Volume != Volume)
        {
            _musicPlayerService.Volume = Volume;
        }
    }

    /// <summary>The configured folder, or an empty string when the platform default is in use.</summary>
    public string MusicFolder
    {
        get => _musicFolderTextBox.Text ?? string.Empty;
        set => _musicFolderTextBox.Text = value ?? string.Empty;
    }

    private void OnMusicFolderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        string value = _musicFolderTextBox.Text?.Trim() ?? string.Empty;
        if (string.Equals(_settingsService.Get(Settings.MusicFolder) ?? string.Empty, value, StringComparison.Ordinal))
        {
            return;
        }

        // The indexing job polls this setting, so writing it here is all that is needed to point the
        // library at a different folder — no rescan has to be triggered from the UI thread.
        _settingsService.Set(Settings.MusicFolder, value);
        UpdateHint();
    }

    public override Task OnInitializeAsync()
    {
        _musicFolderTextBox.Text = _settingsService.Get(Settings.MusicFolder) ?? string.Empty;
        if (_musicPlayerService is not null)
        {
            // Show where the player is now; setting it back to the same value changes nothing.
            _volumeSlider.Value = _musicPlayerService.Volume;
        }

        UpdateHint();
        return base.OnInitializeAsync();
    }

    private void UpdateHint()
    {
        string effective = MusicLibraryService.ResolveMusicFolder(_settingsService);
        _hintLabel.Text = string.IsNullOrWhiteSpace(_musicFolderTextBox.Text)
            ? $"Empty uses the system Music folder: {effective}"
            : (Directory.Exists(effective) ? "Folder found — it is indexed in the background." : "Folder does not exist yet.");
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        _musicFolderLabel.Position = new Position(0, 0);
        _musicFolderLabel.ViewSize = new Size(ViewSize.Width, 1);

        _musicFolderTextBox.Position = new Position(0, 1);
        _musicFolderTextBox.ViewSize = new Size(ViewSize.Width, 1);
        _musicFolderTextBox.ScrollableModel.ViewSize = _musicFolderTextBox.ViewSize;

        _hintLabel.Position = new Position(0, 3);
        _hintLabel.ViewSize = new Size(ViewSize.Width, 1);

        _volumeLabel.Position = new Position(0, 5);
        _volumeLabel.ViewSize = new Size(Math.Min(ViewSize.Width, 7), 1);
        _volumeSlider.Position = new Position(8, 5);
        _volumeSlider.ViewSize = new Size(Math.Max(1, Math.Min(40, (long)ViewSize.Width - 8)), 1);
    }
}
