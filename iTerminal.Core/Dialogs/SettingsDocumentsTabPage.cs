using iConsole.Controls;
using iConsole.Controls.Tabs;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Documents;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The documents folder: the root the indexer walks and the document manager searches. Leaving it
/// empty falls back to the built-in default, which the hint line spells out so an empty box never
/// looks like "nothing is indexed".
///
/// <para>The OCR switch is here rather than in the dialog because it changes what a *scan* does, not
/// what a search does — turning it on only takes effect on the files indexed after it, which is why
/// the hint says so instead of leaving the user to wonder why their scans are still not matching.</para>
/// </summary>
public class SettingsDocumentsTabPage : TabPage
{
    private readonly ISettingsService _settingsService;
    private readonly Label _documentsFolderLabel;
    private readonly TextBox _documentsFolderTextBox;
    private readonly Label _hintLabel;
    private readonly CheckBox _ocrCheckBox;
    private readonly Label _ocrHintLabel;

    public SettingsDocumentsTabPage(
        ISettingsService settingsService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base("5-Documents ", themeService)
    {
        HotConsoleKey = ConsoleKey.D5;
        _settingsService = settingsService ?? NullSettingsService.Instance;

        _documentsFolderLabel = new Label("Documents folder:", themeService);
        AddControl(_documentsFolderLabel);

        _documentsFolderTextBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            MaxLength = 4096,
        };
        _documentsFolderTextBox.PropertyChanged += OnDocumentsFolderChanged;
        AddControl(_documentsFolderTextBox);

        _hintLabel = new Label(string.Empty, themeService);
        AddControl(_hintLabel);

        _ocrCheckBox = new CheckBox(themeService)
        {
            Label = "Read text from images (OCR)",
        };
        _ocrCheckBox.CheckedChanged += OnOcrCheckedChanged;
        AddControl(_ocrCheckBox);

        _ocrHintLabel = new Label(string.Empty, themeService);
        AddControl(_ocrHintLabel);
    }

    /// <summary>The configured folder, or an empty string when the built-in default is in use.</summary>
    public string DocumentsFolder
    {
        get => _documentsFolderTextBox.Text ?? string.Empty;
        set => _documentsFolderTextBox.Text = value ?? string.Empty;
    }

    /// <summary>Whether images are put through OCR during indexing.</summary>
    public bool IsOcrEnabled
    {
        get => _ocrCheckBox.IsChecked;
        set => _ocrCheckBox.IsChecked = value;
    }

    internal string HintForTest => _hintLabel.Text;
    internal string OcrHintForTest => _ocrHintLabel.Text;

    private void OnDocumentsFolderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        string value = _documentsFolderTextBox.Text?.Trim() ?? string.Empty;
        if (string.Equals(_settingsService.Get(Settings.DocumentsFolder) ?? string.Empty, value, StringComparison.Ordinal))
        {
            return;
        }

        // The indexing job polls this setting, so writing it here is all that is needed to point the
        // index at a different folder — no rescan has to be triggered from the UI thread.
        _settingsService.Set(Settings.DocumentsFolder, value);
        UpdateHint();
    }

    private void OnOcrCheckedChanged(object? sender, EventArgs e)
    {
        _settingsService.Set(Settings.DocumentsOcrEnabled, _ocrCheckBox.IsChecked.ToString());
        UpdateHint();
    }

    public override Task OnInitializeAsync()
    {
        _documentsFolderTextBox.Text = _settingsService.Get(Settings.DocumentsFolder) ?? string.Empty;
        _ocrCheckBox.IsChecked = _settingsService.GetAsBool(Settings.DocumentsOcrEnabled) ?? false;
        UpdateHint();
        return base.OnInitializeAsync();
    }

    private void UpdateHint()
    {
        string effective = DocumentLibraryService.ResolveDocumentsFolder(_settingsService);

        _hintLabel.Text = string.IsNullOrWhiteSpace(_documentsFolderTextBox.Text)
            ? $"Empty uses the default folder: {effective}"
            : Directory.Exists(effective)
                ? "Folder found — it is indexed in the background."
                : "Folder does not exist yet.";

        _ocrHintLabel.Text = _ocrCheckBox.IsChecked
            ? "Needs Tesseract on PATH. Applies to images indexed from now on."
            : "Images are indexed by their embedded captions only.";
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        _documentsFolderLabel.Position = new Position(0, 0);
        _documentsFolderLabel.ViewSize = new Size(ViewSize.Width, 1);

        _documentsFolderTextBox.Position = new Position(0, 1);
        _documentsFolderTextBox.ViewSize = new Size(ViewSize.Width, 1);
        _documentsFolderTextBox.ScrollableModel.ViewSize = _documentsFolderTextBox.ViewSize;

        _hintLabel.Position = new Position(0, 3);
        _hintLabel.ViewSize = new Size(ViewSize.Width, 1);

        _ocrCheckBox.Position = new Position(0, 5);
        _ocrCheckBox.ViewSize = new Size(ViewSize.Width, 1);

        _ocrHintLabel.Position = new Position(0, 6);
        _ocrHintLabel.ViewSize = new Size(ViewSize.Width, 1);
    }
}
