using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Factories;
using iTerminal.Core.Helper;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Alt+F5: packs the marked rows — or the one under the cursor — into a new archive, or adds them to
/// one that exists.
///
/// <para>The format follows the archive's name and the name follows the format: typing
/// <c>backup.7z</c> picks 7z, and picking TAR.GZ in the list turns <c>backup.zip</c> into
/// <c>backup.tar.gz</c>. Each format enables only the settings its writer honours — a level for
/// the formats that compress, a password and its cipher for ZIP and 7z, name encryption for 7z —
/// and the hint line says what the levels mean and whether the archive is new or being added
/// to.</para>
/// </summary>
public class CompressDialog : FormDialog
{
    private readonly IContentModelService _contentModelService;
    private readonly IArchiveCompressionService _compressionService;
    private readonly Label _labelSource;
    private readonly FormPanel _form;
    private readonly TextBox _textBoxArchive;
    private readonly ComboBox<ArchiveFormat> _comboFormat;
    private readonly Slider _level;
    private readonly PasswordBox _password;
    private readonly PasswordBox _confirm;
    private readonly ComboBox<ArchiveEncryption> _comboEncryption;
    private readonly CheckBox _checkBoxEncryptNames;
    private readonly CheckBox _checkBoxReplace;
    private readonly Label _labelHint;
    private readonly Label _errorLabel;
    private readonly Button _compressButton;
    private readonly Button _cancelButton;
    private bool _syncing;

    /// <summary>Rows: border, blank, source, six form rows, two check boxes, hint, error, blank, buttons, border.</summary>
    internal const int DialogHeight = 17;

    /// <summary>The panel the sources come from, set by the command that opens the dialog.</summary>
    public IContentModel? SourceModel { get; set; }

    /// <summary>What is packed: the marked rows of <see cref="SourceModel"/>, or the cursor row.</summary>
    public IReadOnlyList<Url> SourceUrls { get; private set; } = [];

    public string ArchivePath
    {
        get => _textBoxArchive.Text ?? string.Empty;
        set => _textBoxArchive.Text = value ?? string.Empty;
    }

    public ArchiveFormat? Format
    {
        get => _comboFormat.SelectedItem;
        set
        {
            var index = _comboFormat.Items.ToList().IndexOf(value!);
            if (index >= 0)
            {
                _comboFormat.SelectedIndex = index;
            }
        }
    }

    public int Level
    {
        get => (int)_level.Value;
        set => _level.Value = value;
    }

    public string Password
    {
        get => _password.Text ?? string.Empty;
        set => _password.Text = value ?? string.Empty;
    }

    public string PasswordConfirmation
    {
        get => _confirm.Text ?? string.Empty;
        set => _confirm.Text = value ?? string.Empty;
    }

    public ArchiveEncryption Encryption
    {
        get => _comboEncryption.SelectedItem;
        set => _comboEncryption.SelectedIndex = _comboEncryption.Items.ToList().IndexOf(value);
    }

    public bool EncryptNames
    {
        get => _checkBoxEncryptNames.IsChecked;
        set => _checkBoxEncryptNames.IsChecked = value;
    }

    public bool ReplaceExisting
    {
        get => _checkBoxReplace.IsChecked;
        set => _checkBoxReplace.IsChecked = value;
    }

    public Label ErrorMessage => _errorLabel;

    internal string HintForTest => _labelHint.Text;
    internal string SourceLabelForTest => _labelSource.Text;
    internal bool IsLevelEnabledForTest => _level.IsEnabled;
    internal bool IsPasswordEnabledForTest => _password.IsEnabled;
    internal bool IsEncryptionEnabledForTest => _comboEncryption.IsEnabled;
    internal bool IsEncryptNamesEnabledForTest => _checkBoxEncryptNames.IsEnabled;

    public CompressDialog(
        IContentModelService contentModelService,
        IArchiveCompressionService compressionService,
        TaskManager taskManager,
        ProgressWindowFactory progressWindowFactory,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService,
        IFileSystemService fileSystemService,
        IMemoryDriveService memoryDriveService,
        ISshFileSystemService sshFileSystemService,
        IArchiveFileSystemService archiveFileSystemService)
        : base(nameof(CompressDialog), themeService)
    {
        _contentModelService = contentModelService;
        _compressionService = compressionService;
        Title = "Compress";

        var compressCommand = new CompressCommand(
            compressionService, taskManager, progressWindowFactory, contentModelService,
            new FileSystemResolver(fileSystemService, memoryDriveService, sshFileSystemService, archiveFileSystemService, contentModelService));
        AddCommand(compressCommand);

        _labelSource = new Label("Source: ", themeService);

        // Built without captions: the form draws them, in one aligned column.
        _textBoxArchive = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            MaxLength = Url.MaxLength,
            IsReadOnly = false,
        };
        _textBoxArchive.PropertyChanged += (_, _) => OnArchivePathChanged();

        _comboFormat = new ComboBox<ArchiveFormat>(themeService) { DisplaySelector = DescribeFormat };
        _comboFormat.SetItems(compressionService.AvailableFormats);
        _comboFormat.SelectionChanged += (_, _) => OnFormatChanged();

        _level = new Slider(themeService) { Step = 1, LargeStep = 3, ShowValue = true };
        _password = new PasswordBox(beepService, dateTimeProviderService, themeService);
        _password.PropertyChanged += (_, _) => UpdateEnabledFields();
        _confirm = new PasswordBox(beepService, dateTimeProviderService, themeService);

        _comboEncryption = new ComboBox<ArchiveEncryption>(themeService) { DisplaySelector = DescribeEncryption };
        _comboEncryption.SetItems([ArchiveEncryption.Aes256, ArchiveEncryption.ZipCrypto]);

        _form = new FormPanel(themeService);
        _form.AddField("Archive", _textBoxArchive);
        _form.AddField("Format", _comboFormat);
        _form.AddField("Level", _level);
        _form.AddField("Password", _password);
        _form.AddField("Confirm", _confirm);
        _form.AddField("Encryption", _comboEncryption);

        _checkBoxEncryptNames = new CheckBox(themeService)
        {
            Label = "Encrypt file names too",
            IsChecked = false,
            HotKeyChar = 'N',
        };
        _checkBoxReplace = new CheckBox(themeService)
        {
            Label = "Replace files the archive already has",
            IsChecked = true,
            HotKeyChar = 'R',
        };
        _labelHint = new Label(string.Empty, themeService);
        _errorLabel = new Label(string.Empty, themeService);
        _compressButton = new Button("Compress", compressCommand, themeService) { HotKeyChar = 'C' };
        _cancelButton = new CancelDialogButton(themeService);

        AddControl(_labelSource);
        AddControl(_form);
        AddControl(_checkBoxEncryptNames);
        AddControl(_checkBoxReplace);
        AddControl(_labelHint);
        AddControl(_errorLabel);
        AddControl(_compressButton);
        AddControl(_cancelButton);
    }

    private static string DescribeFormat(ArchiveFormat format) =>
        format.SupportsPassword ? $"{format.Name}  ({format.Extension}, password)" : $"{format.Name}  ({format.Extension})";

    private static string DescribeEncryption(ArchiveEncryption encryption) => encryption switch
    {
        ArchiveEncryption.Aes256 => "AES-256",
        ArchiveEncryption.ZipCrypto => "ZipCrypto (weak, opens anywhere)",
        _ => encryption.ToString(),
    };

    public override async Task OnInitializeAsync()
    {
        var sourceModel = SourceModel ?? _contentModelService.GetFocusedModel() ?? _contentModelService.GetLastContentModel();
        SourceUrls = FocusedEntry.ActionableUrls(sourceModel);
        _errorLabel.Text = string.Empty;
        Password = string.Empty;
        PasswordConfirmation = string.Empty;

        Title = SourceUrls.Count == 1 ? $"Compress {FocusedEntry.NameOf(SourceUrls[0])}" : $"Compress {SourceUrls.Count} items";
        _labelSource.Text = "Source: " + (SourceUrls.Count == 0 ? string.Empty : TransferDialog.DescribeSources(SourceUrls));

        var format = Format ?? ArchiveFormat.Zip;
        ArchivePath = DefaultArchivePath(SourceUrls, sourceModel?.Url, _contentModelService.NonFocusedFolderUrl, format);
        OnFormatChanged();

        await base.OnInitializeAsync();
    }

    /// <summary>
    /// The other panel's folder, named after the one entry being packed — or after the folder the
    /// entries are in when there are several — with the format's ending.
    /// </summary>
    internal static string DefaultArchivePath(IReadOnlyList<Url> sources, Url? sourceFolder, Url? targetFolder, ArchiveFormat format)
    {
        var baseName = sources.Count == 1
            ? Path.GetFileNameWithoutExtension(FocusedEntry.NameOf(sources[0]))
            : sourceFolder is not null ? FocusedEntry.NameOf(sourceFolder) : "archive";
        if (string.IsNullOrWhiteSpace(baseName) || baseName.EndsWith(':'))
        {
            baseName = "archive";
        }

        var folder = (targetFolder ?? sourceFolder)?.FullUrl ?? string.Empty;
        return UrlPath.Child(folder, baseName + format.Extension, isFolder: false);
    }

    private void OnFormatChanged()
    {
        if (_syncing || Format is not { } format)
        {
            return;
        }

        _syncing = true;
        try
        {
            if (ArchiveFormat.FromFileName(ArchivePath) != format && ArchivePath.Length > 0)
            {
                ArchivePath = format.ApplyExtension(ArchivePath);
                _textBoxArchive.ScrollableModel.CursorPosition.X = ArchivePath.Length;
            }

            _level.Minimum = format.MinLevel;
            _level.Maximum = format.MaxLevel;
            _level.Value = format.DefaultLevel;
            if (format.Encryptions.Count > 0 && !format.Encryptions.Contains(Encryption))
            {
                Encryption = format.Encryptions[0];
            }
        }
        finally
        {
            _syncing = false;
        }

        UpdateEnabledFields();
    }

    private void OnArchivePathChanged()
    {
        if (!_syncing && ArchiveFormat.FromFileName(ArchivePath) is { } typed && typed != Format
            && _comboFormat.Items.Contains(typed))
        {
            Format = typed;
        }

        UpdateEnabledFields();
    }

    /// <summary>Enables what the format honours and says the rest in the hint line.</summary>
    private void UpdateEnabledFields()
    {
        if (Format is not { } format)
        {
            return;
        }

        _level.IsEnabled = format.HasLevels;
        _password.IsEnabled = format.SupportsPassword;
        _confirm.IsEnabled = format.SupportsPassword;
        var hasPassword = format.SupportsPassword && Password.Length > 0;
        _comboEncryption.IsEnabled = hasPassword && format.Encryptions.Count > 1;
        _checkBoxEncryptNames.IsEnabled = hasPassword && format.CanEncryptNames;

        var hint = format.HasLevels ? $"Level: {format.LevelHint}." : format.LevelHint + ".";
        if (!format.SupportsPassword)
        {
            hint += $" {format.Name} takes no password.";
        }

        var unavailable = ArchiveFormat.All.Where(f => !_comboFormat.Items.Contains(f))
            .Select(f => _compressionService.WhyUnavailable(f))
            .FirstOrDefault(reason => reason is not null);
        _labelHint.Text = (ExistsLocally(ArchivePath) ? "Adds to the existing archive. " : string.Empty)
            + hint
            + (unavailable is null ? string.Empty : $" ({unavailable}.)");
        StateHasChanged();
    }

    /// <summary>Only a local archive is looked for: asking a remote drive on every keystroke is a round trip.</summary>
    private static bool ExistsLocally(string path)
    {
        try
        {
            var url = new Url(path);
            return url.ContainerType == ContainerType.FileSystem && File.Exists(url.FullName);
        }
        catch
        {
            return false;
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(new Size(76, DialogHeight));
        }

        var width = new Size(FieldWidth, 1);
        _labelSource.Text = _labelSource.Text.Shrink((int)FieldWidth) ?? string.Empty;
        _labelSource.Position = new Position(FieldX, 2);
        _labelSource.ViewSize = width;
        _form.Position = new Position(FieldX, 4);
        _form.ViewSize = new Size(FieldWidth, 6);
        _checkBoxEncryptNames.Position = new Position(FieldX, 10);
        _checkBoxEncryptNames.ViewSize = width;
        _checkBoxReplace.Position = new Position(FieldX, 11);
        _checkBoxReplace.ViewSize = width;
        _labelHint.Position = new Position(FieldX, 12);
        _labelHint.ViewSize = width;
        _errorLabel.Position = new Position(FieldX, 13);
        _errorLabel.ViewSize = width;
        LayoutButtonRow(_compressButton, _cancelButton);

        base.OnInitializeLayout();
    }

    /// <summary>
    /// The settings as the writer takes them, or the reason they cannot be used. Checked when
    /// Compress is pressed rather than as the fields change, so a half-typed password is not an
    /// error the user has to look at.
    /// </summary>
    internal (CompressionOptions? Options, string? Error) ReadOptions()
    {
        if (SourceUrls.Count == 0)
        {
            return (null, "Nothing to compress.");
        }

        if (string.IsNullOrWhiteSpace(ArchivePath))
        {
            return (null, "Type the archive's name.");
        }

        if (Format is not { } format)
        {
            return (null, "Choose a format.");
        }

        if (ArchiveFormat.FromFileName(ArchivePath) is { } named && named != format)
        {
            return (null, $"The name ends in {named.Extension}, but the format is {format.Name}.");
        }

        var password = format.SupportsPassword ? Password : string.Empty;
        if (password.Length > 0 && password != PasswordConfirmation)
        {
            return (null, "The passwords do not match.");
        }

        return (new CompressionOptions(
            format,
            Level,
            password.Length > 0 ? password : null,
            Encryption,
            EncryptNames && format.CanEncryptNames && password.Length > 0,
            ReplaceExisting), null);
    }

    public void Reset()
    {
        SourceModel = null;
        SourceUrls = [];
        Password = string.Empty;
        PasswordConfirmation = string.Empty;
        _errorLabel.Text = string.Empty;
    }

    /// <summary>Enter, or the Compress button: starts the <see cref="CompressTask"/> and closes the dialog.</summary>
    private sealed class CompressCommand(
        IArchiveCompressionService compressionService,
        TaskManager taskManager,
        ProgressWindowFactory progressWindowFactory,
        IContentModelService contentModelService,
        FileSystemResolver fileSystemResolver) : Command
    {
        public override string Title => "Compress";
        public override string Description => "Write the archive.";

        public override Key? Key { get; init; } = new Key(ConsoleKey.Enter, false, false, false);

        public override async Task ExecuteAsync(object? args)
        {
            var dialog = Control as CompressDialog ?? Control?.Parent as CompressDialog;
            if (dialog is null)
            {
                return;
            }

            var (options, error) = dialog.ReadOptions();
            if (options is null)
            {
                dialog.ErrorMessage.Text = error ?? string.Empty;
                return;
            }

            try
            {
                var archiveUrl = new Url(dialog.ArchivePath);
                var task = new CompressTask(compressionService, dialog.SourceUrls, ResolveFileSystem, archiveUrl, options);

                // Before the task is queued: a small archive is regularly written before Add returns.
                PanelRefreshHelper.RefreshWhenFinished(task, contentModelService, archiveUrl);
                TransferCommand.ConsumeMarks(dialog.SourceModel);

                await taskManager.Add(task);
                await dialog.ShowPopUpWindowAsync(progressWindowFactory.Create(task));
                dialog.Close();
                dialog.Reset();
            }
            catch (Exception exception)
            {
                dialog.ErrorMessage.Text = exception.Message;
            }
        }

        private IFileSystem ResolveFileSystem(Url url) => fileSystemResolver.Resolve(url, operation: "Compressing");
    }
}
