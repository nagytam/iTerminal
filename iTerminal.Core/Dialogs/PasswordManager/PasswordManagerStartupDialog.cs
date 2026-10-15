using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs.PasswordManager;

/// <summary>
/// Asked for at start-up while the password manager is locked: the first run chooses the master password,
/// later runs type it. Enter unlocks (or sets up), Esc, F10 or Ctrl+F10 quits, and so does a third wrong password — the screen
/// never opens with the saved passwords locked. The checkbox keeps the key sealed on this machine for
/// <see cref="RememberFor"/>, after which the password is asked for again.
/// </summary>
public class PasswordManagerStartupDialog : FormDialog
{
    public static readonly TimeSpan RememberFor = TimeSpan.FromDays(30);

    private const int Attempts = 3;
    private const int DialogWidth = 70;

    private readonly IPasswordManager _passwords;
    private readonly bool _setUp;
    private readonly PasswordBox _password;
    private readonly PasswordBox? _confirm;
    private readonly CheckBox _remember;
    private readonly Label _intro;
    private readonly Label _status;
    private readonly Label _hint;
    private readonly FormPanel _form;
    private int _failures;

    /// <summary>Raised once the store is unlocked (or set up). The dialog has closed itself by then.</summary>
    public Action? OnUnlocked { get; set; }

    /// <summary>Raised when the person gives up (Esc, or the last wrong password). The app should not start.</summary>
    public Action? OnStopped { get; set; }

    public PasswordManagerStartupDialog(
        IPasswordManager passwords,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(PasswordManagerStartupDialog), themeService)
    {
        _passwords = passwords;
        _setUp = !passwords.IsSetUp;
        Title = _setUp ? "Password manager set-up" : "Password manager";

        _intro = new Label(
            _setUp
                ? "Choose the master password that unlocks every saved password. It cannot be recovered."
                : "Type the master password to unlock the saved passwords.",
            themeService);

        _password = new PasswordBox(beepService, dateTimeProviderService, themeService);
        _form = new FormPanel(themeService);
        _form.AddField(_setUp ? "New master password" : "Master password", _password);
        if (_setUp)
        {
            _confirm = new PasswordBox(beepService, dateTimeProviderService, themeService);
            _form.AddField("Type it again", _confirm);
        }

        _remember = new CheckBox(themeService)
        {
            Label = $"Remember on this computer for {RememberFor.TotalDays:0} days",
            IsChecked = false,
            HotKeyChar = 'R',
        };
        _status = new Label(
            _setUp ? $"At least {iTerminal.Core.Services.PasswordManager.MinimumMasterPasswordLength} characters." : string.Empty,
            themeService);
        _hint = new Label(_setUp ? "Enter: set up   F10: quit" : "Enter: unlock   F10: quit", themeService);

        AddControl(_intro);
        AddControl(_form);
        AddControl(_remember);
        AddControl(_status);
        AddControl(_hint);
    }

    /// <summary>True once the store is open, so the start-up has nothing to ask.</summary>
    public bool IsUnlocked => _passwords.IsUnlocked;

    #region Test seams

    internal CheckBox RememberForTest => _remember;

    #endregion

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            int fields = _confirm is null ? 1 : 2;
            CenterInParent(new Size(DialogWidth, 11 + (fields - 1)));
        }

        int fieldRows = _confirm is null ? 1 : 2;
        var width = new Size(FieldWidth, 1);
        _intro.Position = new Position(FieldX, 1);
        _intro.ViewSize = width;
        _form.Position = new Position(FieldX, 3);
        _form.ViewSize = new Size(FieldWidth, fieldRows);
        _remember.Position = new Position(FieldX, 3 + fieldRows + 1);
        _remember.ViewSize = width;
        _status.Position = new Position(FieldX, 3 + fieldRows + 3);
        _status.ViewSize = width;
        _hint.Position = new Position(FieldX, ViewSize.Height - 2);
        _hint.ViewSize = width;

        base.OnInitializeLayout();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // F10 is Quit everywhere else. The dialog is modal, so a key it declines never reaches the screen's Quit.
        bool quitKey = keyEvent.ConsoleKey == ConsoleKey.F10 && !keyEvent.Alt && !keyEvent.Shift;
        if (quitKey || (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift))
        {
            Stop();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Control && !keyEvent.Alt)
        {
            Submit();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private void Submit()
    {
        var master = _password.Text;
        if (_setUp)
        {
            SetUp(master);
        }
        else
        {
            Unlock(master);
        }
    }

    private void SetUp(string master)
    {
        if (master.Length < iTerminal.Core.Services.PasswordManager.MinimumMasterPasswordLength)
        {
            _status.Text = $"That is too short. At least {iTerminal.Core.Services.PasswordManager.MinimumMasterPasswordLength} characters.";
            return;
        }

        if (_confirm is not null && _confirm.Text != master)
        {
            _status.Text = "The two do not match. Try again.";
            _password.Text = string.Empty;
            _confirm.Text = string.Empty;
            return;
        }

        _passwords.Setup(master);
        Finish();
    }

    private void Unlock(string master)
    {
        if (_passwords.Unlock(master))
        {
            Finish();
            return;
        }

        _failures++;
        _password.Text = string.Empty;
        if (_failures >= Attempts)
        {
            _status.Text = "The password manager stayed locked, so iTerminal did not start.";
            Stop();
            return;
        }

        _status.Text = $"That is not the master password ({_failures} of {Attempts}).";
    }

    private void Finish()
    {
        if (_remember.IsChecked)
        {
            _passwords.Remember(RememberFor);
        }

        Close();
        OnUnlocked?.Invoke();
    }

    private void Stop()
    {
        Close();
        OnStopped?.Invoke();
    }
}
