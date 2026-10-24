using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.Properties;
using System.Globalization;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Ctrl+F12: what the disk records about the file or folder under the cursor — where it is, how big,
/// its three dates, where it points when it is a link — and the part of that a user can change: the
/// read-only, hidden, system and archive attributes on Windows, the permission bits elsewhere, and the
/// dates (yyyy-MM-dd HH:mm:ss; the created date only on Windows, the others everywhere).
///
/// <para>A folder's size is everything under it, added up on a background task
/// (<see cref="MeasureTask"/>) while the dialog is already up and showing the running total;
/// closing the dialog stops the walk.</para>
/// </summary>
public class PropertiesDialog : FormDialog
{
    private const int CaptionWidth = 11;
    private const int InfoRows = 11;
    private static readonly string[] DateFormats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"];

    private readonly IFilePropertiesService _propertiesService;
    private readonly IContentModelService _contentModelService;
    private readonly InfoLines _info;
    private readonly CheckBox _checkBoxReadOnly;
    private readonly CheckBox _checkBoxHidden;
    private readonly CheckBox _checkBoxSystem;
    private readonly CheckBox _checkBoxArchive;
    private readonly TextBox _textBoxMode;
    private readonly TextBox _textBoxCreated;
    private readonly TextBox _textBoxModified;
    private readonly TextBox _textBoxAccessed;
    private readonly Label _errorLabel;
    private readonly Button _applyButton;
    private readonly Button _cancelButton;
    private readonly bool _usesAttributes = OperatingSystem.IsWindows();

    /// <summary>The date fields this system shows: the created date only on Windows, where it can be set.</summary>
    private TextBox[] DateBoxes => _usesAttributes
        ? [_textBoxCreated, _textBoxModified, _textBoxAccessed]
        : [_textBoxModified, _textBoxAccessed];
    private CancellationTokenSource? _cancellation;
    private FolderMeasure? _measure;
    private bool _measureComplete;

    public PropertiesDialog(
        IFilePropertiesService propertiesService,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(PropertiesDialog), themeService)
    {
        _propertiesService = propertiesService;
        _contentModelService = contentModelService;
        Title = "Properties";
        var applyCommand = new ApplyCommand();
        AddCommand(applyCommand);

        _info = new InfoLines(themeService);
        _checkBoxReadOnly = new CheckBox(themeService) { Label = "Read-only" };
        _checkBoxHidden = new CheckBox(themeService) { Label = "Hidden" };
        _checkBoxSystem = new CheckBox(themeService) { Label = "System" };
        _checkBoxArchive = new CheckBox(themeService) { Label = "Archive" };
        _textBoxMode = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Mode (octal)" };
        _textBoxCreated = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Created" };
        _textBoxModified = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Modified" };
        _textBoxAccessed = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Accessed" };
        _errorLabel = new Label(string.Empty, themeService);
        _applyButton = new Button("Apply", applyCommand, themeService);
        _cancelButton = new CancelDialogButton(themeService);

        // Only the controls this system has a use for are attached at all. Attached and hidden,
        // they would be shown again by ShowAsync, which shows every child.
        if (_usesAttributes)
        {
            AddControl(_checkBoxReadOnly);
            AddControl(_checkBoxHidden);
            AddControl(_checkBoxSystem);
            AddControl(_checkBoxArchive);
        }
        else
        {
            AddControl(_textBoxMode);
        }

        AddControl(_textBoxModified);
        AddControl(_textBoxAccessed);
        if (_usesAttributes)
        {
            AddControl(_textBoxCreated);
        }

        AddControl(_info);
        AddControl(_errorLabel);
        AddControl(_applyButton);
        AddControl(_cancelButton);
    }

    /// <summary>What was read when the dialog opened; null when the entry could not be read.</summary>
    public FileProperties? Properties { get; private set; }

    /// <summary>The folder's contents so far, or in full once <see cref="MeasureTask"/> is done.</summary>
    public FolderMeasure? Measure => _measure;

    /// <summary>The walk adding up a folder; nothing waits on it, it exists so a test can.</summary>
    public Task MeasureTask { get; private set; } = Task.CompletedTask;

    public string Details => _info.Text;

    public string Error => _errorLabel.Text;

    protected override void ClearError() => _errorLabel.Text = string.Empty;

    public bool IsReadOnlyChecked { get => _checkBoxReadOnly.IsChecked; set => _checkBoxReadOnly.IsChecked = value; }
    public bool IsHiddenChecked { get => _checkBoxHidden.IsChecked; set => _checkBoxHidden.IsChecked = value; }
    public bool IsSystemChecked { get => _checkBoxSystem.IsChecked; set => _checkBoxSystem.IsChecked = value; }
    public bool IsArchiveChecked { get => _checkBoxArchive.IsChecked; set => _checkBoxArchive.IsChecked = value; }

    public string Mode
    {
        get => _textBoxMode.Text ?? string.Empty;
        set => _textBoxMode.Text = value ?? string.Empty;
    }

    public string CreatedText { get => _textBoxCreated.Text ?? string.Empty; set => _textBoxCreated.Text = value ?? string.Empty; }

    public string ModifiedText { get => _textBoxModified.Text ?? string.Empty; set => _textBoxModified.Text = value ?? string.Empty; }

    public string AccessedText { get => _textBoxAccessed.Text ?? string.Empty; set => _textBoxAccessed.Text = value ?? string.Empty; }

    private string _path = string.Empty;

    /// <summary>Sets the local file or folder; call before showing the dialog.</summary>
    public void Load(string path)
    {
        _path = path;
        _measure = null;
        _measureComplete = false;
        _errorLabel.Text = string.Empty;

        try
        {
            // One stat: quick enough for the key handler, and the dialog opens already filled in.
            Properties = _propertiesService.Read(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Properties = null;
            _errorLabel.Text = "Error: " + exception.Message;
            return;
        }

        var attributes = Properties.Attributes;
        _checkBoxReadOnly.IsChecked = (attributes & FileAttributes.ReadOnly) != 0;
        _checkBoxHidden.IsChecked = (attributes & FileAttributes.Hidden) != 0;
        _checkBoxSystem.IsChecked = (attributes & FileAttributes.System) != 0;
        _checkBoxArchive.IsChecked = (attributes & FileAttributes.Archive) != 0;
        Mode = Properties.UnixMode is { } mode ? FilePropertiesService.ToOctal(mode) : string.Empty;
        CreatedText = Date(Properties.Created);
        ModifiedText = Date(Properties.Modified);
        AccessedText = Date(Properties.Accessed);
    }

    public override async Task OnInitializeAsync()
    {
        Title = Properties is { } properties ? "Properties of " + properties.Name : "Properties";
        _textBoxMode.ScrollableModel.Reset();
        _textBoxMode.ScrollableModel.ScrollableSize = new Size(Mode.Length, 1);
        _textBoxMode.ScrollableModel.CursorPosition.X = Mode.Length;
        foreach (var box in DateBoxes)
        {
            box.ScrollableModel.Reset();
            box.ScrollableModel.ScrollableSize = new Size(box.Text?.Length ?? 0, 1);
            box.ScrollableModel.CursorPosition.X = box.Text?.Length ?? 0;
        }

        await base.OnInitializeAsync();
        StartMeasuring();
        UpdateLines();
    }

    private void StartMeasuring()
    {
        _cancellation?.Cancel();
        var cancellation = _cancellation = new CancellationTokenSource();

        // A linked folder is not measured: what is behind it is another folder's.
        if (Properties is not { IsFolder: true, LinkTarget: null } properties)
        {
            return;
        }

        MeasureTask = Task.Run(async () =>
        {
            try
            {
                var progress = new InlineProgress(measure => _measure = measure);
                var measure = await _propertiesService.MeasureAsync(properties.Path, progress, cancellation.Token);
                if (!cancellation.IsCancellationRequested)
                {
                    _measure = measure;
                    _measureComplete = true;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _measureComplete = true;
            }
        });
    }

    /// <summary><see cref="Progress{T}"/> posts to a synchronization context; this reports where it is called.</summary>
    private sealed class InlineProgress(Action<FolderMeasure> report) : IProgress<FolderMeasure>
    {
        public void Report(FolderMeasure value) => report(value);
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 8, 50, 86), InfoRows + 9 + DateBoxes.Length));
        }

        _info.Position = new Position(FieldX, 1);
        _info.ViewSize = new Size(FieldWidth, InfoRows);

        var row = InfoRows + 2;
        if (_usesAttributes)
        {
            // Two to a row.
            var half = Math.Max(1, FieldWidth / 2);
            _checkBoxReadOnly.Position = new Position(FieldX, row);
            _checkBoxHidden.Position = new Position(FieldX + half, row);
            _checkBoxSystem.Position = new Position(FieldX, row + 1);
            _checkBoxArchive.Position = new Position(FieldX + half, row + 1);
            foreach (var checkBox in new[] { _checkBoxReadOnly, _checkBoxHidden, _checkBoxSystem, _checkBoxArchive })
            {
                checkBox.ViewSize = new Size(half, 1);
            }
        }
        else
        {
            _textBoxMode.Position = new Position(FieldX, row);
            _textBoxMode.ViewSize = new Size(FieldWidth, 1);
            _textBoxMode.ScrollableModel.ViewSize = _textBoxMode.ViewSize;
        }

        // The dates sit under the attributes or the mode, with the error line under them.
        var dateRow = row + 3;
        foreach (var (box, index) in DateBoxes.Select((box, index) => (box, index)))
        {
            box.Position = new Position(FieldX, dateRow + index);
            box.ViewSize = new Size(FieldWidth, 1);
            box.ScrollableModel.ViewSize = box.ViewSize;
        }

        _errorLabel.Position = new Position(FieldX, dateRow + DateBoxes.Length);
        _errorLabel.ViewSize = new Size(FieldWidth, 1);
        LayoutButtonRow(_applyButton, _cancelButton);
    }

    /// <summary>The folder's total lands on a worker: the idle pass brings the lines in step with it.</summary>
    public override void OnIdle()
    {
        base.OnIdle();
        UpdateLines();
    }

    private static string Row(string caption, string value) => caption.PadRight(CaptionWidth) + value;

    private static string Date(DateTime time) => time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private void UpdateLines()
    {
        if (Properties is not { } properties)
        {
            _info.Lines = [];
            return;
        }

        var valueWidth = Math.Max(10, (int)_info.ViewSize.Width - CaptionWidth);
        var isLink = properties.LinkTarget is not null;
        var kind = (isLink ? "Link to a " : string.Empty) + (properties.IsFolder ? "folder" : "file");
        var lines = new List<InfoLines.Line>
        {
            new(Row("Name", properties.Name.Shrink(valueWidth) ?? string.Empty)),
            new(Row("Location", (Path.GetDirectoryName(properties.Path) ?? string.Empty).Shrink(valueWidth) ?? string.Empty)),
            new(Row("Type", char.ToUpperInvariant(kind[0]) + kind[1..])),
        };

        if (isLink)
        {
            lines.Add(new(Row("Points to", properties.LinkTarget.Shrink(valueWidth) ?? string.Empty)));
        }

        if (properties.Size is { } size)
        {
            lines.Add(new(Row("Size", FilePropertiesService.FormatSize(size))));
        }
        else if (!isLink)
        {
            var measure = _measure ?? FolderMeasure.Empty;
            var contains = $"{FilePropertiesService.Count(measure.Files, "file")}, {FilePropertiesService.Count(measure.Folders, "folder")}";
            lines.Add(new(Row("Size", FilePropertiesService.FormatSize(measure.Bytes) + (_measureComplete ? string.Empty : " …"))));
            lines.Add(new(Row("Contains", contains + (_measureComplete ? string.Empty : " so far"))));
            if (measure.Unreadable > 0)
            {
                // A total that left folders out is a lower bound, and has to say so.
                lines.Add(new(Row(string.Empty, $"at least — {FilePropertiesService.Count(measure.Unreadable, "folder")} could not be read"), ThemeService.CurrentColorScheme.Error));
            }
        }

        lines.Add(new(Row("Created", Date(properties.Created))));
        lines.Add(new(Row("Modified", Date(properties.Modified))));
        lines.Add(new(Row("Accessed", Date(properties.Accessed))));
        if (properties.UnixMode is { } mode)
        {
            lines.Add(new(Row("Permissions", FilePropertiesService.ToSymbolic(mode))));
        }

        _info.Lines = lines;
    }

    /// <summary>
    /// Writes the attributes, the mode and the dates that differ from what was read, and closes. False,
    /// with <see cref="Error"/> saying why, when a date or the mode is not one or the disk refuses. Nothing is written when a
    /// date does not parse.
    /// </summary>
    public bool Apply()
    {
        if (Properties is not { } properties)
        {
            Close();
            return true;
        }

        if (!TryChangedDate(CreatedText, properties.Created, out var created)
            || !TryChangedDate(ModifiedText, properties.Modified, out var modified)
            || !TryChangedDate(AccessedText, properties.Accessed, out var accessed))
        {
            _errorLabel.Text = "Error: a date is yyyy-MM-dd HH:mm:ss, such as 2024-05-01 10:20:30.";
            return false;
        }

        try
        {
            if (_usesAttributes)
            {
                var wanted = (IsReadOnlyChecked ? FileAttributes.ReadOnly : 0)
                    | (IsHiddenChecked ? FileAttributes.Hidden : 0)
                    | (IsSystemChecked ? FileAttributes.System : 0)
                    | (IsArchiveChecked ? FileAttributes.Archive : 0);
                if (wanted != (properties.Attributes & FilePropertiesService.ChangeableAttributes))
                {
                    _propertiesService.Apply(properties.Path, wanted, unixMode: null);
                }
            }
            else
            {
                if (FilePropertiesService.ParseOctal(Mode) is not { } mode)
                {
                    _errorLabel.Text = "Error: the mode is three or four octal digits, such as 644 or 755.";
                    return false;
                }

                if (mode != properties.UnixMode)
                {
                    _propertiesService.Apply(properties.Path, attributes: null, mode);
                }
            }

            if (created is not null || modified is not null || accessed is not null)
            {
                _propertiesService.SetTimes(properties.Path, created, modified, accessed);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _errorLabel.Text = "Error: " + exception.Message;
            return false;
        }

        Close();
        RefreshPanels();
        return true;
    }

    /// <summary>
    /// The date the text names when it differs from the one the entry has; null when it is the same. False when the text is
    /// not a date. The disk keeps finer than the text shows, so the comparison is to the second.
    /// </summary>
    private static bool TryChangedDate(string text, DateTime current, out DateTime? changed)
    {
        changed = null;
        if (!DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return false;
        }

        if (Date(time) != Date(current))
        {
            changed = time;
        }

        return true;
    }

    private void RefreshPanels() => PanelRefreshHelper.RefreshLocalPanels(_contentModelService);

    public override void Close()
    {
        // Nobody is left to read the total, and the walk can be of a whole disk.
        _cancellation?.Cancel();
        base.Close();
    }

    /// <summary>Enter, or the Apply button.</summary>
    private sealed class ApplyCommand : Command
    {
        public override string Title => "Apply";
        public override string Description => "Write the changed attributes and close.";

        public override Key? Key { get; init; } =
            new Key(ConsoleKey.Enter, false, false, false);

        public override Task ExecuteAsync(object? args)
        {
            var dialog = Control as PropertiesDialog ?? Control?.Parent as PropertiesDialog;
            dialog?.Apply();
            return Task.CompletedTask;
        }
    }
}
