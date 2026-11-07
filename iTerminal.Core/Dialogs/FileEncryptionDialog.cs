using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Helper;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Encryption;
using iTerminal.Core.Services.Wipe;

namespace iTerminal.Core.Dialogs;

/// <summary>Where <see cref="FileEncryptionDialog"/> is: typing the passphrase, working, asking about the originals, deleting them, or over.</summary>
public enum EncryptionPhase
{
    Entering,
    Running,
    AskDelete,
    Deleting,
    Finished,
}

/// <summary>
/// Ctrl+Shift+E: encrypts the marked files (or the one under the cursor) to separate <c>.enc</c> files with a passphrase, or
/// decrypts <c>.enc</c> files back (<see cref="IFileEncryptionService"/>).
///
/// <list type="bullet">
/// <item><b>The passphrase lives only in the masked boxes</b> and is cleared when the run ends. It is typed twice to encrypt,
/// is never stored, logged, shown or put in a URL or on a command line.</item>
/// <item><b>The run is on a background task</b> with a byte count; closing the dialog cancels it, and a half-written output is deleted.</item>
/// <item><b>Nothing is overwritten.</b> An output that exists makes that file fail; the others still run.</item>
/// <item><b>The originals are never deleted silently.</b> After a run that wrote something, a separate y/n asks; yes moves them to
/// the recycle bin where there is one, and otherwise overwrites and removes them (the secure wipe).</item>
/// </list>
/// </summary>
public class FileEncryptionDialog : FormDialog
{
    private readonly IFileEncryptionService _encryption;
    private readonly IFileSystemService _fileSystem;
    private readonly ISecureWipeService _wipeService;
    private readonly INotificationService _notifications;
    private readonly IContentModelService _contentModelService;
    private readonly Label _labelSource;
    private readonly FormPanel _form;
    private readonly ComboBox<EncryptionMode> _comboMode;
    private readonly PasswordBox _passphrase;
    private readonly PasswordBox _confirm;
    private readonly Label _labelHint;
    private readonly Label _labelHint2;
    private readonly Label _labelStatus;
    private readonly Label _errorLabel;
    private readonly Button _runButton;
    private readonly Button _cancelButton;
    private CancellationTokenSource? _cancellation;
    private long _bytesDone;
    private long _bytesTotal;
    private volatile string _currentFile = string.Empty;
    private volatile EncryptionResult? _result;
    private volatile string _deleteSummary = string.Empty;
    private volatile bool _refreshPending;
    private volatile bool _focusPending;
    private bool _canRecycle;
    private volatile EncryptionPhase _phase = EncryptionPhase.Entering;

    internal const int DialogHeight = 16;

    public FileEncryptionDialog(
        IFileEncryptionService encryption,
        IFileSystemService fileSystem,
        ISecureWipeService wipeService,
        INotificationService notifications,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(FileEncryptionDialog), themeService)
    {
        _encryption = encryption;
        _fileSystem = fileSystem;
        _wipeService = wipeService;
        _notifications = notifications;
        _contentModelService = contentModelService;
        Title = UiText.Get("Dialog.FileEncryption", "Encrypt / decrypt files");

        var runCommand = new RunCommand();
        AddCommand(runCommand);

        _labelSource = new Label(string.Empty, themeService);
        _comboMode = new ComboBox<EncryptionMode>(themeService) { DisplaySelector = DescribeMode };
        _comboMode.SetItems([EncryptionMode.Encrypt, EncryptionMode.Decrypt]);
        _comboMode.SelectionChanged += (_, _) => UpdateMode();
        _passphrase = new PasswordBox(beepService, dateTimeProviderService, themeService);
        _confirm = new PasswordBox(beepService, dateTimeProviderService, themeService);

        _form = new FormPanel(themeService);
        _form.AddField(UiText.Get("Field.Mode", "Mode"), _comboMode);
        _form.AddField(UiText.Get("Field.Passphrase", "Passphrase"), _passphrase);
        _form.AddField(UiText.Get("Field.PassphraseConfirm", "Confirm"), _confirm);

        _labelHint = new Label(string.Empty, themeService);
        _labelHint2 = new Label(string.Empty, themeService);
        _labelStatus = new Label(string.Empty, themeService);
        _errorLabel = new Label(string.Empty, themeService);
        _runButton = new Button(UiText.Get("Encryption.Run", "Run"), runCommand, themeService) { HotKeyChar = HotKeys.VisibleIn(UiText.Get("Encryption.Run", "Run"), 'R') };
        _cancelButton = new CancelDialogButton(themeService);

        AddControl(_labelSource);
        AddControl(_form);
        AddControl(_labelHint);
        AddControl(_labelHint2);
        AddControl(_labelStatus);
        AddControl(_errorLabel);
        AddControl(_runButton);
        AddControl(_cancelButton);
    }

    private static string DescribeMode(EncryptionMode mode) => mode == EncryptionMode.Encrypt
        ? UiText.Get("Encryption.ModeEncrypt", "Encrypt")
        : UiText.Get("Encryption.ModeDecrypt", "Decrypt");

    /// <summary>What will be processed.</summary>
    public IReadOnlyList<string> Paths { get; private set; } = [];

    public EncryptionMode Mode
    {
        get => _comboMode.SelectedItem;
        set => _comboMode.SelectedIndex = value == EncryptionMode.Encrypt ? 0 : 1;
    }

    public string Passphrase
    {
        get => _passphrase.Text ?? string.Empty;
        set => _passphrase.Text = value ?? string.Empty;
    }

    public string PassphraseConfirmation
    {
        get => _confirm.Text ?? string.Empty;
        set => _confirm.Text = value ?? string.Empty;
    }

    public EncryptionPhase Phase => _phase;

    /// <summary>The outcome of the run, once it is over.</summary>
    public EncryptionResult? Result => _result;

    /// <summary>The run started by Run; nothing waits on it, it exists so a test can.</summary>
    public Task RunTask { get; private set; } = Task.CompletedTask;

    /// <summary>The delete started by answering y; likewise for a test.</summary>
    public Task DeleteTask { get; private set; } = Task.CompletedTask;

    public Label ErrorMessage => _errorLabel;

    /// <summary>The status line as drawn.</summary>
    internal string StatusForTest => _labelStatus.Text;

    /// <summary>Whether the passphrase box has the focus; a test waits for it before typing.</summary>
    public bool IsPassphraseFocused => _passphrase.IsFocused;

    internal bool IsConfirmEnabledForTest => _confirm.IsEnabled;

    protected override void ClearError() => _errorLabel.Text = string.Empty;

    /// <summary>
    /// Sets what to process; call before showing the dialog. The mode starts as Decrypt when every file already ends in
    /// <c>.enc</c>, as Encrypt otherwise; the Mode box can change it.
    /// </summary>
    public void Load(IEnumerable<string> paths)
    {
        _cancellation?.Cancel();
        _cancellation = null;
        _focusPending = true;
        _comboMode.IsEnabled = true;
        _passphrase.IsEnabled = true;
        Paths = [.. paths];
        _canRecycle = Paths.Count > 0 && DeleteDialog.CanRecycle(new Url(Paths[0]));
        _phase = EncryptionPhase.Entering;
        _result = null;
        _deleteSummary = string.Empty;
        Interlocked.Exchange(ref _bytesDone, 0);
        Interlocked.Exchange(ref _bytesTotal, 0);
        _currentFile = string.Empty;
        Passphrase = string.Empty;
        PassphraseConfirmation = string.Empty;
        _errorLabel.Text = string.Empty;
        _sourceText = DescribeSources();
        _labelSource.Text = _sourceText;
        Mode = Paths.Count > 0 && Paths.All(path => path.EndsWith(FileEncryptionService.Extension, StringComparison.OrdinalIgnoreCase))
            ? EncryptionMode.Decrypt
            : EncryptionMode.Encrypt;
        UpdateMode();
    }

    private string _sourceText = string.Empty;

    private string DescribeSources()
    {
        var names = Paths.Select(path => Path.GetFileName(path.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : path).ToList();
        return UiText.Get("Label.Source", "Source") + ": " + (names.Count == 1 ? names[0] : string.Join(", ", names));
    }

    private void UpdateMode()
    {
        var encrypt = Mode == EncryptionMode.Encrypt;
        _confirm.IsEnabled = encrypt && _phase == EncryptionPhase.Entering;
        _hintText = encrypt
            ? UiText.Get("Encryption.HintEncrypt", "Writes name.enc beside each file; the passphrase is never stored.")
            : UiText.Get("Encryption.HintDecrypt", "Writes the original beside each .enc file; nothing is overwritten.");
        _hint2Text = encrypt
            ? UiText.Get("Encryption.HintLose", "Lose the passphrase and the data cannot be recovered.")
            : string.Empty;
        ShowHints();
        StateHasChanged();
    }

    private string _hintText = string.Empty;
    private string _hint2Text = string.Empty;

    /// <summary>The two notes under the fields, each ending on an ellipsis where the dialog is too narrow for it.</summary>
    private void ShowHints()
    {
        _labelHint.Text = Fitted(_hintText, _labelHint.ViewSize.Width);
        _labelHint2.Text = Fitted(_hint2Text, _labelHint2.ViewSize.Width);
    }

    private static string Fitted(string text, long width) => width > 1 && text.Length > width ? text[..(int)(width - 1)] + "…" : text;

    /// <summary>The settings as the run takes them, or the reason it cannot start. Checked when Run is pressed.</summary>
    internal string? Validate()
    {
        if (Paths.Count == 0)
        {
            return UiText.Get("Encryption.Nothing", "Nothing to process: mark files first.");
        }

        if (Passphrase.Length == 0)
        {
            return UiText.Get("Encryption.EmptyPassphrase", "Type a passphrase.");
        }

        if (Mode == EncryptionMode.Encrypt && Passphrase != PassphraseConfirmation)
        {
            return UiText.Get("Encryption.Mismatch", "The passphrases do not match.");
        }

        return null;
    }

    /// <summary>Enter or the Run button: validates, then starts the run on a background task. Does nothing once a run has started.</summary>
    public void Start()
    {
        if (_phase != EncryptionPhase.Entering)
        {
            return;
        }

        if (Validate() is { } error)
        {
            _errorLabel.Text = error;
            return;
        }

        var passphrase = Passphrase;
        var mode = Mode;
        var paths = Paths;
        // The boxes are emptied now: the typed text is not kept on screen or in a control while the work runs.
        Passphrase = string.Empty;
        PassphraseConfirmation = string.Empty;
        _phase = EncryptionPhase.Running;
        _comboMode.IsEnabled = false;
        _passphrase.IsEnabled = false;
        _confirm.IsEnabled = false;
        _errorLabel.Text = string.Empty;

        var cancellation = _cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(p =>
        {
            Interlocked.Exchange(ref _bytesDone, p.BytesDone);
            Interlocked.Exchange(ref _bytesTotal, p.BytesTotal);
            _currentFile = p.CurrentFile;
        });
        RunTask = Task.Run(async () =>
        {
            EncryptionResult result;
            try
            {
                result = await _encryption.ProcessAsync(paths, mode, passphrase, progress, cancellation.Token);
            }
            catch (Exception exception)
            {
                result = new EncryptionResult([new EncryptionItem(paths.FirstOrDefault() ?? string.Empty, null, exception.Message)], Cancelled: false);
            }

            Announce(result);
            // A run the dialog was closed on and reopened over must not write into the new session.
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _result = result;
                _refreshPending = true;
                _phase = result.Succeeded > 0 && !result.Cancelled ? EncryptionPhase.AskDelete : EncryptionPhase.Finished;
            }
        });
    }

    /// <summary>The answer to "delete the originals?": yes starts the delete, no keeps them.</summary>
    public void AnswerDelete(bool delete)
    {
        if (_phase != EncryptionPhase.AskDelete)
        {
            return;
        }

        if (!delete)
        {
            _deleteSummary = UiText.Get("Encryption.Kept", "The originals were kept.");
            _phase = EncryptionPhase.Finished;
            return;
        }

        var sources = (_result?.Items ?? []).Where(item => item.Succeeded).Select(item => item.Source).ToList();
        _phase = EncryptionPhase.Deleting;
        var token = (_cancellation ??= new CancellationTokenSource()).Token;
        var current = _cancellation;
        DeleteTask = Task.Run(async () =>
        {
            var failures = new List<string>();
            var deleted = 0;
            foreach (string source in sources)
            {
                try
                {
                    var url = new Url(source);
                    if (DeleteDialog.CanRecycle(url))
                    {
                        if (_fileSystem.DeleteFile(url, moveToRecycleBin: true))
                        {
                            deleted++;
                        }
                    }
                    else
                    {
                        var wiped = await _wipeService.WipeAsync([source], WipePattern.Random, null, token);
                        deleted += wiped.Files;
                        failures.AddRange(wiped.Failures);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
                {
                    failures.Add($"{Path.GetFileName(source)}: {exception.Message}");
                }
            }

            if (!ReferenceEquals(_cancellation, current))
            {
                return;
            }

            _deleteSummary = failures.Count == 0
                ? string.Format(UiText.Get("Encryption.Deleted", "{0} original(s) deleted."), deleted)
                : string.Format(UiText.Get("Encryption.DeleteFailed", "{0} original(s) deleted; {1} failed: {2}"), deleted, failures.Count, failures[0]);
            _refreshPending = true;
            _phase = EncryptionPhase.Finished;
        });
    }

    private void Announce(EncryptionResult result)
    {
        if (result.Cancelled)
        {
            _notifications.Notify(
                UiText.Get("Notify.EncryptionStopped", "File encryption stopped"),
                string.Format(UiText.Get("Notify.EncryptionStoppedBody", "{0} file(s) done before it was cancelled."), result.Succeeded),
                NotificationSeverity.Warning);
        }
        else if (result.Failed > 0)
        {
            _notifications.Notify(
                UiText.Get("Notify.EncryptionErrors", "File encryption finished with errors"),
                string.Format(UiText.Get("Notify.EncryptionErrorsBody", "{0} done; {1} failed. First: {2}"), result.Succeeded, result.Failed, result.Items.First(item => !item.Succeeded).Error),
                NotificationSeverity.Error);
        }
        else
        {
            _notifications.Notify(
                UiText.Get("Notify.EncryptionFinished", "File encryption finished"),
                string.Format(UiText.Get("Notify.EncryptionFinishedBody", "{0} file(s) done."), result.Succeeded),
                NotificationSeverity.Success);
        }
    }

    private sealed class InlineProgress(Action<EncryptionProgress> report) : IProgress<EncryptionProgress>
    {
        public void Report(EncryptionProgress value) => report(value);
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            CenterInParent(new Size(72, DialogHeight));
        }

        var width = new Size(FieldWidth, 1);
        _labelSource.Text = _sourceText.Shrink(Math.Max(16, (int)FieldWidth)) ?? _sourceText;
        _labelSource.Position = new Position(FieldX, 2);
        _labelSource.ViewSize = width;
        _form.Position = new Position(FieldX, 4);
        _form.ViewSize = new Size(FieldWidth, 3);
        _labelHint.Position = new Position(FieldX, 8);
        _labelHint.ViewSize = width;
        _labelHint2.Position = new Position(FieldX, 9);
        _labelHint2.ViewSize = width;
        ShowHints();
        _labelStatus.Position = new Position(FieldX, 11);
        _labelStatus.ViewSize = width;
        _errorLabel.Position = new Position(FieldX, 12);
        _errorLabel.ViewSize = width;
        LayoutButtonRow(_runButton, _cancelButton);
        base.OnInitializeLayout();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        if (_focusPending)
        {
            // The dialog is reused: start in the passphrase box, not wherever the last use left the focus.
            _focusPending = false;
            _passphrase.SetFocus();
        }

        if (_refreshPending)
        {
            _refreshPending = false;
            PanelRefreshHelper.RefreshLocalPanels(_contentModelService);
        }

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var width = Math.Max(16, (int)FieldWidth);
        string status;
        switch (_phase)
        {
            case EncryptionPhase.Running:
                {
                    var done = Interlocked.Read(ref _bytesDone);
                    var total = Interlocked.Read(ref _bytesTotal);
                    var percent = total > 0 ? Math.Min(100, done * 100 / total) : 0;
                    status = string.Format(UiText.Get("Encryption.Working", "Working… {0}%  {1}"), percent, Path.GetFileName(_currentFile));
                    break;
                }
            case EncryptionPhase.AskDelete:
                status = string.Format(
                    UiText.Get(_canRecycle ? "Encryption.AskDelete" : "Encryption.AskDeleteWipe",
                        _canRecycle
                            ? "Delete the {0} original file(s)? They go to the recycle bin.  y/n"
                            : "Delete the {0} original file(s)? No recycle bin: overwritten and removed for good.  y/n"),
                    _result?.Succeeded ?? 0);
                break;
            case EncryptionPhase.Deleting:
                status = UiText.Get("Encryption.Deleting", "Deleting the originals…");
                break;
            case EncryptionPhase.Finished when _result is { } result:
                status = result.Cancelled
                    ? string.Format(UiText.Get("Encryption.Stopped", "Stopped: {0} file(s) done."), result.Succeeded)
                    : string.Format(UiText.Get("Encryption.Done", "Done: {0} file(s), {1} failed."), result.Succeeded, result.Failed);
                if (_deleteSummary.Length > 0)
                {
                    status += "  " + _deleteSummary;
                }

                if (result.Failed > 0 && _errorLabel.Text.Length == 0)
                {
                    var first = result.Items.First(item => !item.Succeeded);
                    var message = result.Items.Count == 1 ? first.Error! : $"{Path.GetFileName(first.Source)}: {first.Error}";
                    _errorLabel.Text = message.Shrink(width) ?? message;
                }

                break;
            default:
                status = string.Empty;
                break;
        }

        _labelStatus.Text = status.Shrink(width) ?? status;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        switch (_phase)
        {
            case EncryptionPhase.Entering:
                yield return ("Enter", UiText.Get("DialogHint.EncryptionRun", "Encrypt or decrypt with the passphrase"));
                yield return ("Tab", UiText.Get("DialogHint.EncryptionNextField", "Next field"));
                break;
            case EncryptionPhase.Running:
                yield return ("Esc", UiText.Get("DialogHint.EncryptionStop", "Stop; files already done stay done"));
                break;
            case EncryptionPhase.AskDelete:
                yield return ("y", UiText.Get("DialogHint.EncryptionDeleteOriginals", "Delete the originals"));
                yield return ("n", UiText.Get("DialogHint.EncryptionKeepOriginals", "Keep the originals"));
                break;
            case EncryptionPhase.Finished:
                yield return ("Esc", UiText.Get("Common.Close", "Close"));
                break;
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_phase == EncryptionPhase.AskDelete && keyEvent.KeyPressType == KeyPressType.Down && !keyEvent.Control && !keyEvent.Alt)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Y:
                    AnswerDelete(true);
                    return true;
                case ConsoleKey.N:
                    AnswerDelete(false);
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Close()
    {
        // Stops after the chunk under way; the half-written output is deleted, finished files stay.
        _cancellation?.Cancel();
        Passphrase = string.Empty;
        PassphraseConfirmation = string.Empty;
        base.Close();
    }

    /// <summary>Enter, or the Run button.</summary>
    private sealed class RunCommand : Command
    {
        public override string Title => "Run";
        public override string Description => "Encrypt or decrypt the files with the passphrase.";

        public override Key? Key { get; init; } = new Key(ConsoleKey.Enter, false, false, false);

        public override Task ExecuteAsync(object? args)
        {
            var dialog = Control as FileEncryptionDialog ?? Control?.Parent as FileEncryptionDialog;
            dialog?.Start();
            return Task.CompletedTask;
        }
    }
}
