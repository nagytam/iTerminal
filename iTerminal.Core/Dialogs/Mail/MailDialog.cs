using iConsole.Localization;
using iTerminal.Core.Helper;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Model.Mail;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Mail;

namespace iTerminal.Core.Dialogs.Mail;

/// <summary>
/// A mail client for one account: the inbox over POP3, messages written and sent over SMTP, and recipients
/// found in an LDAP address book. Five screens share the dialog — the list of messages, the reader, the
/// compose form, the address-book search and the account setup — and every server conversation runs on a
/// background task with its result shown in the footer, so a slow server never holds the keyboard.
/// Deleting a message removes it from the server, so D asks once more before it does.
/// </summary>
public class MailDialog : Dialog
{
    private enum Mode
    {
        Setup,
        List,
        View,
        Compose,
        Directory,
    }

    private sealed class SetupField(string label, bool isToggle = false, bool isSecret = false)
    {
        public string Label { get; } = label;
        public bool IsToggle { get; } = isToggle;
        public bool IsSecret { get; } = isSecret;
        public string Text { get; set; } = string.Empty;
        public bool On { get; set; }
    }

    private const int DialogWidth = 80;
    private const int DialogHeight = 26;
    private const int ListRows = 18;
    private const int MaxDraftLength = 4000;
    private const int MaxFieldLength = 200;
    private const int ReplyDateLength = 16;

    private readonly MailAccountStore _accounts;
    private readonly IMailService _mail;
    private readonly IPasswordManager _passwords;
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellation = new();

    private Mode _mode = Mode.List;
    private MailAccount? _account;
    private IReadOnlyList<MailListing> _listings = [];
    private int _cursor;
    private int? _pendingDelete;
    private MailItem? _item;
    private List<string> _bodyLines = [];
    private int _scroll;

    /// <summary>To, Subject and Body, in that order, and the field that takes the typing.</summary>
    private readonly string[] _draft = ["", "", ""];
    private int _draftField;

    private string _directoryTerm = string.Empty;
    private List<DirectoryPerson> _people = [];
    private int _personCursor;

    private SetupField[] _setup = [];
    private int _setupCursor;

    private string _status = string.Empty;
    private bool _statusIsError;
    private bool _busy;
    private Task? _work;

    public MailDialog(MailAccountStore accounts, IMailService mail, IPasswordManager passwords, IThemeService themeService)
        : base(nameof(MailDialog), themeService)
    {
        Title = iConsole.Localization.UiText.Get("Dialog.Mail", "Mail");
        _accounts = accounts;
        _mail = mail;
        _passwords = passwords;
    }

    /// <summary>
    /// The dialog is built once and opened many times. Each opening re-reads the account, and goes straight to
    /// the setup form when there is none, or to the inbox when there is.
    /// </summary>
    public override async Task ShowAsync()
    {
        lock (_sync)
        {
            _account = _accounts.Accounts.FirstOrDefault();
            _pendingDelete = null;
            if (_account is null)
            {
                OpenSetup(null);
            }
            else if (_mode != Mode.View && _mode != Mode.Compose && _mode != Mode.Directory)
            {
                _mode = Mode.List;
            }
        }

        await base.ShowAsync();
        if (_account is not null && _mode == Mode.List && _listings.Count == 0 && !_busy)
        {
            RefreshInbox();
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(DialogWidth, DialogHeight));
        }

        base.OnInitializeLayout();
    }

    public override void Close()
    {
        _cancellation.Cancel();
        base.Close();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        switch (_mode)
        {
            case Mode.List:
                yield return ("Up/Down", UiText.Get("DialogHint.ChooseMessage", "Choose a message"));
                yield return ("Enter", UiText.Get("DialogHint.ReadMessage", "Read the message"));
                yield return ("C", UiText.Get("DialogHint.WriteMessage", "Write a message"));
                yield return ("R", UiText.Get("DialogHint.RefreshInbox", "Refresh the inbox"));
                yield return ("D/Delete", UiText.Get("DialogHint.DeleteMessageAsks", "Delete the message (asks first)"));
                yield return ("S", UiText.Get("DialogHint.AccountSettings", "Account settings"));
                break;
            case Mode.View:
                yield return ("Up/Down", UiText.Get("DialogHint.ScrollMessage", "Scroll the message"));
                yield return ("R", UiText.Get("DialogHint.Reply", "Reply"));
                yield return ("Esc", UiText.Get("DialogHint.BackToInbox", "Back to the inbox"));
                break;
            case Mode.Compose:
                yield return ("Ctrl+S", UiText.Get("DialogHint.SendMessage", "Send the message"));
                yield return ("Ctrl+L", UiText.Get("DialogHint.LookUpAddress", "Look up an address"));
                yield return ("Tab", UiText.Get("DialogHint.MoveBetweenFields", "Move between the fields"));
                yield return ("Esc", UiText.Get("DialogHint.BackToInbox", "Back to the inbox"));
                break;
            default:
                yield return ("Esc", iConsole.Localization.UiText.Get("Common.Back", "Back"));
                break;
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        lock (_sync)
        {
            switch (_mode)
            {
                case Mode.Setup:
                    return HandleSetupKey(keyEvent);
                case Mode.List:
                    if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
                    {
                        break;  // the dialog closes on Esc
                    }

                    return HandleListKey(keyEvent);
                case Mode.View:
                    return HandleViewKey(keyEvent);
                case Mode.Compose:
                    return HandleComposeKey(keyEvent);
                case Mode.Directory:
                    return HandleDirectoryKey(keyEvent);
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    #region Keys

    private bool HandleListKey(KeyEvent keyEvent)
    {
        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        if (keyEvent.ConsoleKey != ConsoleKey.Delete && keyEvent.ConsoleKey != ConsoleKey.D)
        {
            _pendingDelete = null;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow when plain:
                return MoveCursor(-1);
            case ConsoleKey.DownArrow when plain:
                return MoveCursor(1);
            case ConsoleKey.PageUp when plain:
                return MoveCursor(-ListRows);
            case ConsoleKey.PageDown when plain:
                return MoveCursor(ListRows);
            case ConsoleKey.Home when plain:
                return MoveCursor(-int.MaxValue / 2);
            case ConsoleKey.End when plain:
                return MoveCursor(int.MaxValue / 2);
            case ConsoleKey.Enter when plain:
                ReadSelected();
                return true;
        }

        if (!plain)
        {
            return false;
        }

        switch (char.ToLowerInvariant(keyEvent.KeyChar))
        {
            case 'r':
                RefreshInbox();
                return true;
            case 'c':
                OpenCompose(string.Empty, string.Empty, string.Empty);
                return true;
            case 's':
                OpenSetup(_account);
                return true;
            case 'd':
                DeleteSelected();
                return true;
        }

        return false;
    }

    private bool HandleViewKey(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            _mode = Mode.List;
            StateHasChanged();
            return true;
        }

        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow when plain:
                _scroll = Math.Max(0, _scroll - 1);
                StateHasChanged();
                return true;
            case ConsoleKey.DownArrow when plain:
                _scroll = Math.Min(Math.Max(0, _bodyLines.Count - 1), _scroll + 1);
                StateHasChanged();
                return true;
            case ConsoleKey.PageUp when plain:
                _scroll = Math.Max(0, _scroll - ListRows);
                StateHasChanged();
                return true;
            case ConsoleKey.PageDown when plain:
                _scroll = Math.Min(Math.Max(0, _bodyLines.Count - 1), _scroll + ListRows);
                StateHasChanged();
                return true;
            case ConsoleKey.Home when plain:
                _scroll = 0;
                StateHasChanged();
                return true;
            case ConsoleKey.End when plain:
                _scroll = Math.Max(0, _bodyLines.Count - 1);
                StateHasChanged();
                return true;
        }

        if (plain && char.ToLowerInvariant(keyEvent.KeyChar) == 'r' && _item is { } item)
        {
            OpenCompose(ReplyAddress(item.From), ReplySubject(item.Subject), string.Empty);
            return true;
        }

        return false;
    }

    private bool HandleComposeKey(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            _mode = Mode.List;
            StateHasChanged();
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.S)
        {
            Send();
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.L)
        {
            OpenDirectory();
            return true;
        }

        if (PasteHelper.IsDeleteWordKey(keyEvent) || PasteHelper.IsClearKey(keyEvent))
        {
            _draft[_draftField] = PasteHelper.IsClearKey(keyEvent) ? string.Empty : PasteHelper.DeleteLastWord(_draft[_draftField]);
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            // The body keeps the clipboard's line breaks; the address and subject are one line.
            PasteHelper.Paste(
                text => AppendToDraft(_draftField == 2 ? text.Replace("\r\n", "\n").Replace('\r', '\n') : PasteHelper.SingleLine(text)),
                StateHasChanged);
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Tab)
        {
            _draftField = (_draftField + (keyEvent.Shift ? _draft.Length - 1 : 1)) % _draft.Length;
            StateHasChanged();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Control && !keyEvent.Alt)
        {
            if (_draftField == 2)
            {
                AppendToDraft("\n");
            }
            else
            {
                _draftField++;
            }

            StateHasChanged();
            return true;
        }

        if (keyEvent.Control || keyEvent.Alt)
        {
            return false;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Backspace)
        {
            var text = _draft[_draftField];
            if (text.Length > 0)
            {
                _draft[_draftField] = text[..^1];
            }

            StateHasChanged();
            return true;
        }

        if (IsPrintable(keyEvent))
        {
            AppendToDraft(keyEvent.KeyChar.ToString());
            StateHasChanged();
        }

        return true;
    }

    private bool HandleDirectoryKey(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            _mode = Mode.Compose;
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsDeleteWordKey(keyEvent) || PasteHelper.IsClearKey(keyEvent))
        {
            _directoryTerm = PasteHelper.IsClearKey(keyEvent) ? string.Empty : PasteHelper.DeleteLastWord(_directoryTerm);
            _people = [];
            _personCursor = 0;
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            PasteHelper.Paste(text =>
            {
                _directoryTerm = (_directoryTerm + PasteHelper.SingleLine(text)) is { Length: > MaxFieldLength } longer ? longer[..MaxFieldLength] : _directoryTerm + PasteHelper.SingleLine(text);
                _people = [];
                _personCursor = 0;
            }, StateHasChanged);
            return true;
        }

        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Enter when _people.Count > 0 && plain:
                PickPerson();
                return true;
            case ConsoleKey.Enter:
                SearchDirectory();
                return true;
            case ConsoleKey.UpArrow when plain:
                _personCursor = Math.Max(0, _personCursor - 1);
                StateHasChanged();
                return true;
            case ConsoleKey.DownArrow when plain:
                _personCursor = Math.Min(Math.Max(0, _people.Count - 1), _personCursor + 1);
                StateHasChanged();
                return true;
            case ConsoleKey.Backspace:
                if (_directoryTerm.Length > 0)
                {
                    _directoryTerm = _directoryTerm[..^1];
                    _people = [];
                }

                StateHasChanged();
                return true;
        }

        if (!keyEvent.Control && !keyEvent.Alt && IsPrintable(keyEvent) && _directoryTerm.Length < MaxFieldLength)
        {
            _directoryTerm += keyEvent.KeyChar;
            _people = [];
            _personCursor = 0;
            StateHasChanged();
        }

        return true;
    }

    private bool HandleSetupKey(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            if (_account is null)
            {
                Close();
            }
            else
            {
                _mode = Mode.List;
                StateHasChanged();
            }

            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.S)
        {
            SaveSetup();
            return true;
        }

        if ((PasteHelper.IsDeleteWordKey(keyEvent) || PasteHelper.IsClearKey(keyEvent)) && !_setup[_setupCursor].IsToggle)
        {
            SetupField edited = _setup[_setupCursor];
            edited.Text = PasteHelper.IsClearKey(keyEvent) ? string.Empty : PasteHelper.DeleteLastWord(edited.Text);
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            SetupField target = _setup[_setupCursor];
            if (!target.IsToggle)
            {
                PasteHelper.Paste(text =>
                {
                    string line = PasteHelper.SingleLine(text);
                    target.Text = (target.Text + line) is { Length: > MaxFieldLength } longer ? longer[..MaxFieldLength] : target.Text + line;
                }, StateHasChanged);
            }

            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Tab when keyEvent.Shift:
                _setupCursor = (_setupCursor - 1 + _setup.Length) % _setup.Length;
                StateHasChanged();
                return true;
            case ConsoleKey.Tab:
            case ConsoleKey.DownArrow:
                _setupCursor = (_setupCursor + 1) % _setup.Length;
                StateHasChanged();
                return true;
            case ConsoleKey.UpArrow:
                _setupCursor = (_setupCursor - 1 + _setup.Length) % _setup.Length;
                StateHasChanged();
                return true;
            case ConsoleKey.Enter:
                _setupCursor = (_setupCursor + 1) % _setup.Length;
                StateHasChanged();
                return true;
        }

        if (keyEvent.Control || keyEvent.Alt)
        {
            return true;
        }

        var field = _setup[_setupCursor];
        if (field.IsToggle)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.Spacebar || keyEvent.KeyChar == ' ')
            {
                field.On = !field.On;
                StateHasChanged();
            }

            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Backspace)
        {
            if (field.Text.Length > 0)
            {
                field.Text = field.Text[..^1];
            }
        }
        else if (IsPrintable(keyEvent) && field.Text.Length < MaxFieldLength)
        {
            field.Text += keyEvent.KeyChar;
        }

        StateHasChanged();
        return true;
    }

    private static bool IsPrintable(KeyEvent keyEvent) =>
        keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar);

    private void AppendToDraft(string text)
    {
        var current = _draft[_draftField];
        if (current.Length + text.Length <= MaxDraftLength)
        {
            _draft[_draftField] = current + text;
        }
    }

    #endregion

    #region Actions

    /// <summary>
    /// The wheel moves the highlight in the inbox (three messages), scrolls an open message (three lines) and moves
    /// through the address-book results; a click highlights a message or a person.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool handled = false;
        lock (_sync)
        {
            if (!AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                handled = false;
            }
            else if (mouseEvent.WheelDelta != 0)
            {
                int step = mouseEvent.WheelDelta > 0 ? -3 : 3;
                switch (_mode)
                {
                    case Mode.List:
                        handled = MoveCursor(step);
                        break;
                    case Mode.View:
                        _scroll = Math.Clamp(_scroll + step, 0, Math.Max(0, _bodyLines.Count - 1));
                        StateHasChanged();
                        handled = true;
                        break;
                    case Mode.Directory when _people.Count > 0:
                        _personCursor = Math.Clamp(_personCursor + step, 0, _people.Count - 1);
                        StateHasChanged();
                        handled = true;
                        break;
                }
            }
            else if (mouseEvent.LeftClicked)
            {
                long row = mouseEvent.Position.Y - (AbsolutePosition.Y + 3);
                long column = mouseEvent.Position.X - (AbsolutePosition.X + 2);
                int visible = (int)ViewSize.Height - 7;
                if (column >= 0 && column < ViewSize.Width - 4 && row >= 0 && row < visible)
                {
                    if (_mode == Mode.List)
                    {
                        int first = _cursor >= visible ? _cursor - visible + 1 : 0;
                        if (first + (int)row < _listings.Count)
                        {
                            handled = MoveCursor(first + (int)row - _cursor);
                        }
                    }
                    else if (_mode == Mode.Directory && row < _people.Count)
                    {
                        _personCursor = (int)row;
                        StateHasChanged();
                        handled = true;
                    }
                }
            }
        }

        return handled || await base.HandleMouseEventAsync(mouseEvent);
    }

    private bool MoveCursor(int delta)
    {
        _pendingDelete = null;
        if (_listings.Count > 0)
        {
            _cursor = Math.Clamp(_cursor + delta, 0, _listings.Count - 1);
        }

        StateHasChanged();
        return true;
    }

    private void RefreshInbox()
    {
        var account = _account;
        if (account is null)
        {
            return;
        }

        StartWork("Reading the inbox…", async ct =>
        {
            var listings = await _mail.ListAsync(account, PasswordOf(account), ct);
            lock (_sync)
            {
                _listings = listings;
                _cursor = Math.Clamp(_cursor, 0, Math.Max(0, listings.Count - 1));
                SetStatus(listings.Count == 0 ? "The inbox is empty." : $"{listings.Count} messages. Enter reads one.", false);
            }
        });
    }

    private void ReadSelected()
    {
        var account = _account;
        if (account is null || _cursor < 0 || _cursor >= _listings.Count)
        {
            return;
        }

        var number = _listings[_cursor].Number;
        StartWork($"Reading message {number}…", async ct =>
        {
            var item = await _mail.ReadAsync(account, PasswordOf(account), number, ct);
            lock (_sync)
            {
                _item = item;
                _bodyLines = item.Body.Replace("\r\n", "\n").Split('\n').ToList();
                _scroll = 0;
                _mode = Mode.View;
                SetStatus(string.Empty, false);
            }
        });
    }

    /// <summary>D asks once, D again removes the message from the server. Any other key cancels the question.</summary>
    private void DeleteSelected()
    {
        var account = _account;
        if (account is null || _cursor < 0 || _cursor >= _listings.Count)
        {
            return;
        }

        var listing = _listings[_cursor];
        if (_pendingDelete != listing.Number)
        {
            _pendingDelete = listing.Number;
            SetStatus($"Press D again to delete \"{listing.Subject}\" from the server.", true);
            StateHasChanged();
            return;
        }

        _pendingDelete = null;
        StartWork($"Deleting message {listing.Number}…", async ct =>
        {
            var password = PasswordOf(account);
            await _mail.DeleteAsync(account, password, listing.Number, ct);
            var listings = await _mail.ListAsync(account, password, ct);
            lock (_sync)
            {
                _listings = listings;
                _cursor = Math.Clamp(_cursor, 0, Math.Max(0, listings.Count - 1));
                SetStatus("Deleted.", false);
            }
        });
    }

    private void OpenCompose(string to, string subject, string body)
    {
        _draft[0] = to;
        _draft[1] = subject;
        _draft[2] = body;
        _draftField = string.IsNullOrEmpty(to) ? 0 : 1;
        _mode = Mode.Compose;
        SetStatus(string.Empty, false);
        StateHasChanged();
    }

    private void Send()
    {
        var account = _account;
        if (account is null)
        {
            return;
        }

        var to = _draft[0].Trim();
        if (to.Length == 0)
        {
            SetStatus("Give the message a recipient first.", true);
            _draftField = 0;
            StateHasChanged();
            return;
        }

        var subject = _draft[1];
        var body = _draft[2];
        StartWork("Sending…", async ct =>
        {
            await _mail.SendAsync(account, PasswordOf(account), to, subject, body, ct);
            lock (_sync)
            {
                _draft[0] = _draft[1] = _draft[2] = string.Empty;
                _mode = Mode.List;
                SetStatus($"Sent to {to}.", false);
            }
        });
    }

    private void OpenDirectory()
    {
        // The name being typed is the last recipient after the final comma.
        var to = _draft[0];
        var comma = to.LastIndexOf(',');
        _directoryTerm = (comma < 0 ? to : to[(comma + 1)..]).Trim();
        _people = [];
        _personCursor = 0;
        _mode = Mode.Directory;
        SetStatus(_account is null ? string.Empty : "Type a name or address, then Enter.", false);
        StateHasChanged();
    }

    private void SearchDirectory()
    {
        var account = _account;
        if (account is null || _directoryTerm.Trim().Length == 0)
        {
            return;
        }

        var term = _directoryTerm.Trim();
        StartWork("Searching the address book…", async ct =>
        {
            var people = await _mail.SearchDirectoryAsync(account, PasswordOf(account), term, ct);
            lock (_sync)
            {
                _people = people.ToList();
                _personCursor = 0;
                SetStatus(_people.Count == 0 ? $"Nobody matches \"{term}\"." : $"{_people.Count} found. Enter adds the highlighted one.", false);
            }
        });
    }

    private void PickPerson()
    {
        if (_personCursor < 0 || _personCursor >= _people.Count)
        {
            return;
        }

        var person = _people[_personCursor];
        var to = _draft[0].TrimEnd();
        var comma = to.LastIndexOf(',');
        var prefix = comma < 0 ? string.Empty : to[..(comma + 1)].TrimEnd() + " ";
        _draft[0] = prefix + person.AsRecipient;
        _draftField = 1;
        _mode = Mode.Compose;
        SetStatus(string.Empty, false);
        StateHasChanged();
    }

    /// <summary>The setup form is the account: a blank password on a saved account keeps the one in the vault.</summary>
    private void OpenSetup(MailAccount? account)
    {
        _setup =
        [
            new SetupField("Account name") { Text = account?.Name ?? string.Empty },
            new SetupField("Incoming (POP3)") { Text = account?.PopHost ?? string.Empty },
            new SetupField("POP3 port") { Text = (account?.PopPort ?? 995).ToString() },
            new SetupField("POP3 SSL", isToggle: true) { On = account?.PopTls ?? true },
            new SetupField("Outgoing (SMTP)") { Text = account?.SmtpHost ?? string.Empty },
            new SetupField("SMTP port") { Text = (account?.SmtpPort ?? 587).ToString() },
            new SetupField("SMTP STARTTLS", isToggle: true) { On = account?.SmtpTls ?? true },
            new SetupField("User name") { Text = account?.UserName ?? string.Empty },
            new SetupField("Password", isSecret: true),
            new SetupField("From address") { Text = account?.FromAddress ?? string.Empty },
            new SetupField("Address book (LDAP)") { Text = account?.LdapHost ?? string.Empty },
            new SetupField("LDAP port") { Text = (account?.LdapPort ?? 636).ToString() },
            new SetupField("LDAP SSL", isToggle: true) { On = account?.LdapTls ?? true },
            new SetupField("LDAP base DN") { Text = account?.LdapBaseDn ?? string.Empty },
            new SetupField("LDAP bind DN") { Text = account?.LdapBindDn ?? string.Empty },
        ];
        _setupCursor = 0;
        _mode = Mode.Setup;
        SetStatus(account is null ? "Set up an account: incoming, outgoing and address book servers." : "Change the account, then Ctrl+S to save.", false);
        StateHasChanged();
    }

    private void SaveSetup()
    {
        var f = _setup;
        if (!int.TryParse(f[2].Text, out var popPort) || !int.TryParse(f[5].Text, out var smtpPort) || !int.TryParse(f[11].Text, out var ldapPort))
        {
            SetStatus("The ports must be numbers.", true);
            StateHasChanged();
            return;
        }

        if (f[1].Text.Trim().Length == 0 && f[4].Text.Trim().Length == 0)
        {
            SetStatus("Give at least the incoming (POP3) or the outgoing (SMTP) server.", true);
            StateHasChanged();
            return;
        }

        var account = new MailAccount
        {
            Id = _account?.Id ?? Guid.NewGuid().ToString("N"),
            Name = f[0].Text.Trim(),
            PopHost = f[1].Text.Trim(),
            PopPort = popPort,
            PopTls = f[3].On,
            SmtpHost = f[4].Text.Trim(),
            SmtpPort = smtpPort,
            SmtpTls = f[6].On,
            UserName = f[7].Text.Trim(),
            FromAddress = f[9].Text.Trim(),
            LdapHost = f[10].Text.Trim(),
            LdapPort = ldapPort,
            LdapTls = f[12].On,
            LdapBaseDn = f[13].Text.Trim(),
            LdapBindDn = f[14].Text.Trim(),
        };

        if (f[8].Text.Length > 0)
        {
            _passwords.Save(new PasswordEntry
            {
                Id = account.SecretProfileId,
                Title = string.Format(UiText.Get("Dialog.MailAccountTitle", "Mail: {0}"), account.Name),
                Username = account.UserName,
                Category = PasswordCategory.Mail,
                Password = f[8].Text,
            });
        }

        _accounts.Save(account);
        _account = account;
        _listings = [];
        _cursor = 0;
        _mode = Mode.List;
        SetStatus("Saved.", false);
        RefreshInbox();
    }

    private string PasswordOf(MailAccount account) => _passwords.GetSecret(account.SecretProfileId) ?? string.Empty;

    private static string ReplyAddress(string from)
    {
        var lt = from.IndexOf('<');
        var gt = from.IndexOf('>');
        return lt >= 0 && gt > lt ? from[(lt + 1)..gt] : from.Trim();
    }

    private static string ReplySubject(string subject) =>
        subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? subject : "Re: " + subject;

    /// <summary>
    /// Runs one server conversation on a background task. Only one runs at a time; the result is written
    /// under the lock, and a failure replaces the footer's text with the server's reason.
    /// </summary>
    private void StartWork(string busyText, Func<CancellationToken, Task> work)
    {
        lock (_sync)
        {
            if (_busy)
            {
                SetStatus("Still working on the last request.", true);
                StateHasChanged();
                return;
            }

            _busy = true;
            SetStatus(busyText, false);
        }

        var token = _cancellation.Token;
        _work = Task.Run(async () =>
        {
            try
            {
                await work(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The dialog closed while the server was being asked; there is no one to tell.
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    SetStatus(ex.Message, true);
                }
            }
            finally
            {
                lock (_sync)
                {
                    _busy = false;
                }
            }
        });
    }

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
    }

    #endregion

    #region Drawing

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        lock (_sync)
        {
            for (var row = 0; row < ih; row++)
            {
                Put(buffer, 0, row, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
            }

            var contentRows = ih - 3;
            switch (_mode)
            {
                case Mode.Setup:
                    DrawSetup(buffer, iw, colorScheme);
                    break;
                case Mode.List:
                    DrawList(buffer, iw, contentRows, colorScheme);
                    break;
                case Mode.View:
                    DrawView(buffer, iw, contentRows, colorScheme);
                    break;
                case Mode.Compose:
                    DrawCompose(buffer, iw, contentRows, colorScheme);
                    break;
                case Mode.Directory:
                    DrawDirectory(buffer, iw, contentRows, colorScheme);
                    break;
            }

            var statusColor = _statusIsError ? colorScheme.Error : Color.Cyan;
            Put(buffer, 1, ih - 2, iw - 2, _status, statusColor, colorScheme.Background);
            Put(buffer, 1, ih - 1, iw - 2, Hints(), colorScheme.Foreground, colorScheme.Background);
        }
    }

    private string Hints() => _mode switch
    {
        Mode.Setup => "Tab/Up/Down field  Space toggle  Ctrl+V paste  Ctrl+S save  Esc back",
        Mode.List => "Enter read  R refresh  C compose  D delete  S setup  Esc close",
        Mode.View => "Up/Down scroll  R reply  Esc back to the list",
        Mode.Compose => "Tab field  Enter newline  Ctrl+V paste  Ctrl+S send  Ctrl+L address book  Esc back",
        _ => "Type a name, Enter search, Enter again adds the highlighted person  Esc back",
    };

    private void DrawList(IBuffer buffer, int iw, int rows, ColorScheme colorScheme)
    {
        Put(buffer, 1, 0, iw - 2, $"Inbox — {_account?.Name ?? string.Empty} {_account?.UserName}", Color.Cyan, colorScheme.Background);
        Put(buffer, 1, 1, iw - 2, "  Date              From                       Subject", colorScheme.DisabledForeground, colorScheme.Background);
        if (_listings.Count == 0)
        {
            Put(buffer, 1, 2, iw - 2, "No messages. R reads the inbox again.", colorScheme.DisabledForeground, colorScheme.Background);
            return;
        }

        var first = _cursor >= rows - 2 ? _cursor - (rows - 2) + 1 : 0;
        for (var i = 0; i < rows - 2; i++)
        {
            var index = first + i;
            var y = 2 + i;
            if (index >= _listings.Count)
            {
                Put(buffer, 1, y, iw - 2, string.Empty, colorScheme.Foreground, colorScheme.Background);
                continue;
            }

            var listing = _listings[index];
            var highlighted = index == _cursor;
            var line = $"{Clip(listing.Date, ReplyDateLength),-16}  {Clip(listing.From, 24),-24}   {listing.Subject}";
            var foreground = highlighted ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
            var background = highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
            Put(buffer, 1, y, iw - 2, (highlighted ? "> " : "  ") + line, foreground, background);
        }
    }

    private void DrawView(IBuffer buffer, int iw, int rows, ColorScheme colorScheme)
    {
        if (_item is null)
        {
            return;
        }

        Put(buffer, 1, 0, iw - 2, "From:    " + _item.From, Color.Cyan, colorScheme.Background);
        Put(buffer, 1, 1, iw - 2, "To:      " + _item.To, colorScheme.Foreground, colorScheme.Background);
        Put(buffer, 1, 2, iw - 2, "Subject: " + _item.Subject, colorScheme.Foreground, colorScheme.Background);
        Put(buffer, 1, 3, iw - 2, "Date:    " + _item.Date, colorScheme.DisabledForeground, colorScheme.Background);
        var bodyRows = rows - 5;
        for (var i = 0; i < bodyRows; i++)
        {
            var index = _scroll + i;
            var text = index < _bodyLines.Count ? _bodyLines[index] : string.Empty;
            Put(buffer, 1, 5 + i, iw - 2, text, colorScheme.Foreground, colorScheme.Background);
        }
    }

    private void DrawCompose(IBuffer buffer, int iw, int rows, ColorScheme colorScheme)
    {
        DrawField(buffer, iw, 0, "To:      ", _draft[0], _draftField == 0, colorScheme);
        DrawField(buffer, iw, 1, "Subject: ", _draft[1], _draftField == 1, colorScheme);
        Put(buffer, 1, 2, iw - 2, "Body:", _draftField == 2 ? Color.Cyan : colorScheme.DisabledForeground, colorScheme.Background);

        var lines = _draft[2].Split('\n');
        var bodyRows = rows - 3;
        var first = Math.Max(0, lines.Length - bodyRows);
        for (var i = 0; i < bodyRows; i++)
        {
            var index = first + i;
            var text = index < lines.Length ? lines[index] : string.Empty;
            if (_draftField == 2 && index == lines.Length - 1 && i == Math.Min(bodyRows, lines.Length) - 1)
            {
                text += "_";
            }

            Put(buffer, 1, 3 + i, iw - 2, text, colorScheme.Foreground, colorScheme.Background);
        }
    }

    private void DrawField(IBuffer buffer, int iw, int row, string label, string text, bool focused, ColorScheme colorScheme)
    {
        Put(buffer, 1, row, 10, label, focused ? Color.Cyan : colorScheme.DisabledForeground, colorScheme.Background);
        Put(buffer, 11, row, iw - 12, text + (focused ? "_" : string.Empty), colorScheme.Foreground, focused ? colorScheme.SelectedBackground : colorScheme.Background);
    }

    private void DrawDirectory(IBuffer buffer, int iw, int rows, ColorScheme colorScheme)
    {
        Put(buffer, 1, 0, iw - 2, "Search: " + _directoryTerm + "_", colorScheme.SelectedFocusedForeground, colorScheme.Background);
        if (_people.Count == 0)
        {
            Put(buffer, 1, 2, iw - 2, _directoryTerm.Length == 0 ? "Type part of a name or an address." : "Press Enter to search the address book.", colorScheme.DisabledForeground, colorScheme.Background);
            return;
        }

        for (var i = 0; i < rows - 2 && i < _people.Count; i++)
        {
            var person = _people[i];
            var highlighted = i == _personCursor;
            var line = $"{Clip(person.Name, 28),-28}  {Clip(person.Mail, 30),-30}  {person.Phone}";
            Put(buffer, 1, 2 + i, iw - 2, (highlighted ? "> " : "  ") + line,
                highlighted ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground,
                highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background);
        }
    }

    private void DrawSetup(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        for (var i = 0; i < _setup.Length; i++)
        {
            var field = _setup[i];
            var focused = i == _setupCursor;
            Put(buffer, 1, i, 22, field.Label, focused ? Color.Cyan : colorScheme.DisabledForeground, colorScheme.Background);
            var value = field.IsToggle ? (field.On ? "[x] yes" : "[ ] no")
                : field.IsSecret ? new string('*', field.Text.Length)
                : field.Text;
            Put(buffer, 23, i, iw - 24, value + (focused && !field.IsToggle ? "_" : string.Empty), colorScheme.Foreground,
                focused ? colorScheme.SelectedBackground : colorScheme.Background);
        }
    }

    private void Put(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background)
    {
        if (width <= 0)
        {
            return;
        }

        var clipped = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1 + x, AbsolutePosition.Y + 1 + y),
            new Size(width, 1),
            new ConxelString(clipped, foreground, background));
    }

    private static string Clip(string text, int width) => text.Length <= width ? text : text[..(width - 1)] + "…";

    #endregion

    #region Test seams

    internal Task? WorkForTest => _work;
    internal bool IsBusyForTest { get { lock (_sync) return _busy; } }
    internal string StatusForTest { get { lock (_sync) return _status; } }
    internal string ModeForTest { get { lock (_sync) return _mode.ToString(); } }
    internal int ListingCountForTest { get { lock (_sync) return _listings.Count; } }
    internal string BodyForTest { get { lock (_sync) return string.Join("\n", _bodyLines); } }
    internal string[] DraftForTest { get { lock (_sync) return [.. _draft]; } }
    internal int PersonCursorForTest { get { lock (_sync) return _personCursor; } }
    internal int CursorForTest { get { lock (_sync) return _cursor; } }
    internal int ScrollForTest { get { lock (_sync) return _scroll; } }
    internal int DraftFieldForTest { get { lock (_sync) return _draftField; } }
    internal int SetupCursorForTest { get { lock (_sync) return _setupCursor; } }
    internal string DirectoryTermForTest { get { lock (_sync) return _directoryTerm; } }
    internal IReadOnlyList<DirectoryPerson> PeopleForTest { get { lock (_sync) return _people.ToList(); } }
    internal string SetupValueForTest(string label)
    {
        lock (_sync)
        {
            var field = _setup.First(f => f.Label == label);
            return field.IsToggle ? (field.On ? "yes" : "no") : field.Text;
        }
    }

    #endregion
}
