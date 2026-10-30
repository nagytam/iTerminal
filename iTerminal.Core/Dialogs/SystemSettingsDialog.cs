using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Services.SystemSettings;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Ctrl+Shift+F10: the settings of the machine itself — its name, workgroup and description, the
/// environment variables of the user and of the machine, and the time zone. Left/Right (or Tab) change
/// section, the arrows move, Enter edits the highlighted row.
///
/// <para>Nothing is read or written from a key handler: the data is gathered on a task when the dialog
/// opens, and each change runs on one, with the answer (or the operating system's refusal) shown on the
/// bottom row. A change that needs administrator rights and finds none is refused by the service with a
/// sentence saying how to get them, and the dialog shows at the top whether it has them.</para>
///
/// <para>The one-line input (a new name, a variable's value) is the same box the Git client uses and is
/// tracked by <c>_inputActive</c>, not by its visibility: <c>ShowAsync</c> makes every child visible on
/// the way in.</para>
/// </summary>
public class SystemSettingsDialog : Dialog
{
    public enum SettingsSection
    {
        Computer,
        Environment,
        TimeZone,
    }

    private static readonly string[] SectionNames = ["Computer", "Environment", "Time zone"];

    private const int HeaderRows = 3; // Section strip, administrator banner, section caption.
    private const int FooterRows = 1; // The message.

    private readonly ISystemSettingsService _service;
    private readonly InfoLines _lines;
    private readonly TextBox _input;

    private SettingsSection _section;
    private readonly int[] _cursors = new int[3];
    private EnvironmentScope _scope = EnvironmentScope.User;
    private ComputerIdentity? _identity;
    private IReadOnlyList<SystemEnvironmentVariable> _variables = [];
    private bool _variablesLoaded;
    private IReadOnlyList<TimeZoneChoice> _zones = [];
    private string _currentZone = string.Empty;

    private string _message = string.Empty;
    private bool _messageIsError;

    /// <summary>Set when the first read failed: what would have been read is then "not available", not "being read".</summary>
    private bool _loadFailed;
    private bool _busy;
    private int _environmentVersion;

    private bool _inputActive;
    private Func<string, Task>? _onSubmit;
    private string? _confirmPrompt;
    private Func<Task>? _onConfirm;

    public SystemSettingsDialog(
        ISystemSettingsService service,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(SystemSettingsDialog), themeService)
    {
        _service = service;
        Title = iConsole.Localization.UiText.Get("Dialog.SystemSettings", "System Settings");
        _lines = new InfoLines(themeService);
        _input = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            IsVisible = false,
            IsEnabled = false,
            CanFocus = false,
        };
        AddControl(_lines);
        AddControl(_input);
    }

    /// <summary>The data gathering the dialog last started. Nothing awaits it — a test can.</summary>
    internal Task LoadTask { get; private set; } = Task.CompletedTask;

    /// <summary>The change last started. Nothing awaits it — a test can.</summary>
    internal Task PendingOperation { get; private set; } = Task.CompletedTask;

    internal SettingsSection Section => _section;

    internal int Cursor => _cursors[(int)_section];

    internal EnvironmentScope Scope => _scope;

    internal bool InputActive => _inputActive;

    internal string Message => _message;

    /// <summary>What the dialog shows, as text, for a test to read.</summary>
    internal string Text => _lines.Text;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        ApplyInputVisibility();
        _message = "Reading the system settings…";
        Render();
        LoadTask = Task.Run(async () =>
        {
            try
            {
                _currentZone = _service.CurrentTimeZoneId;
                _zones = _service.GetTimeZones();
                _identity = await _service.GetComputerAsync().ConfigureAwait(false);
                _message = string.Empty;
                Render();
                await LoadEnvironmentAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Said on the dialog. Left alone, "Reading the system settings…" and the "Reading…" rows stayed for ever.
                _loadFailed = true;
                _message = $"Could not read the system settings: {exception.Message}";
                _messageIsError = true;
                Render();
            }
        });
    }

    private async Task LoadEnvironmentAsync()
    {
        var version = Interlocked.Increment(ref _environmentVersion);
        var scope = _scope;
        var variables = await _service.GetEnvironmentAsync(scope).ConfigureAwait(false);
        if (version != _environmentVersion)
        {
            return; // The scope was switched again while this was reading.
        }

        _variables = variables;
        _variablesLoaded = true;
        _cursors[(int)SettingsSection.Environment] = Math.Clamp(_cursors[(int)SettingsSection.Environment], 0, Math.Max(0, variables.Count - 1));
        Render();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(
                Math.Min(100, Math.Max(40, Parent.ViewSize.Width - 4)),
                Math.Min(30, Math.Max(12, Parent.ViewSize.Height - 2))));
        }

        _lines.Position = new Position(1, 1);
        _lines.ViewSize = new Size(Math.Max(1, ViewSize.Width - 2), Math.Max(1, ViewSize.Height - 3));
        _input.Position = new Position(1, Math.Max(1, ViewSize.Height - 2));
        _input.ViewSize = new Size(Math.Max(1, ViewSize.Width - 2), 1);
        _input.ScrollableModel.ViewSize = _input.ViewSize;

        base.OnInitializeLayout();
        Render();
    }

    // ── Drawing ─────────────────────────────────────────────────────────────

    private int ContentRows => Math.Max(1, (int)_lines.ViewSize.Height - HeaderRows - FooterRows);

    private void Render()
    {
        var scheme = ThemeService.CurrentColorScheme;
        var lines = new List<InfoLines.Line>
        {
            new(string.Join("  ", SectionNames.Select((name, i) => i == (int)_section ? $"[{name}]" : $" {name} ")), Color.Yellow),
            _service.IsElevated
                ? new("Running as administrator.", null)
                : new("Not administrator: machine-wide changes are refused. Ctrl+Shift+A restarts iTerminal as administrator.", Color.DarkYellow),
        };

        var body = new List<InfoLines.Line>();
        switch (_section)
        {
            case SettingsSection.Computer:
                lines.Add(new("The name this machine goes by on the network.", scheme.DisabledForeground));
                BuildComputer(body, scheme);
                break;
            case SettingsSection.Environment:
                lines.Add(new(
                    _service.SupportsEnvironment(_scope)
                        ? $"Scope: {(_scope == EnvironmentScope.User ? "User" : "Machine (all users)")}  -  {(_variablesLoaded ? _variables.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : "reading")} variables"
                        : "Environment variables cannot be changed on this system.",
                    scheme.DisabledForeground));
                BuildEnvironment(body, scheme);
                break;
            default:
                lines.Add(new($"Current: {_currentZone}", scheme.DisabledForeground));
                BuildTimeZones(body);
                break;
        }

        while (body.Count < ContentRows)
        {
            body.Add(default);
        }

        lines.AddRange(body.Take(ContentRows));
        lines.Add(MessageLine(scheme));
        _lines.Lines = lines;
        StateHasChanged();
    }

    private InfoLines.Line MessageLine(ColorScheme scheme)
    {
        if (_confirmPrompt is not null)
        {
            return new(_confirmPrompt + " (y/n)", Color.Yellow);
        }

        if (_busy)
        {
            return new("Working…", Color.Yellow);
        }

        return new(_message, _messageIsError ? Color.DarkRed : scheme.Foreground);
    }

    private void BuildComputer(List<InfoLines.Line> body, ColorScheme scheme)
    {
        if (_identity is not { } identity)
        {
            body.Add(new(_loadFailed ? "  Not available" : "  Reading…", scheme.DisabledForeground));
            return;
        }

        var membershipLabel = identity.IsDomainMember ? "Domain" : "Workgroup";
        (string Label, string Value, bool Editable)[] rows =
        [
            ("Computer name", identity.ComputerName, true),
            (membershipLabel, identity.Membership, identity.CanChangeMembership),
            ("Description", identity.Description, identity.CanChangeDescription),
        ];

        for (var i = 0; i < rows.Length; i++)
        {
            var (label, value, editable) = rows[i];
            var pointer = i == _cursors[0] ? "▶" : " ";
            var shown = value.Length == 0 ? "(not set)" : value;
            var color = i == _cursors[0] ? Color.Yellow : editable ? (Color?)null : scheme.DisabledForeground;
            body.Add(new($"{pointer} {label,-15}: {shown}{(editable ? string.Empty : "   (read-only)")}", color));
        }

        body.Add(default);
        body.Add(new("  A new computer name or workgroup takes effect after a restart.", scheme.DisabledForeground));
        if (identity.IsDomainMember)
        {
            body.Add(new("  Leaving a domain needs a directory account: use Windows' System Properties.", scheme.DisabledForeground));
        }
    }

    private void BuildEnvironment(List<InfoLines.Line> body, ColorScheme scheme)
    {
        if (!_service.SupportsEnvironment(_scope))
        {
            return;
        }

        if (!_variablesLoaded)
        {
            body.Add(new(_loadFailed ? "  Not available" : "  Reading…", scheme.DisabledForeground));
            return;
        }

        if (_variables.Count == 0)
        {
            body.Add(new("  (no variables - Ins adds one)", scheme.DisabledForeground));
            return;
        }

        var cursor = _cursors[(int)SettingsSection.Environment];
        var nameWidth = Math.Min(32, _variables.Max(v => v.Name.Length));
        foreach (var index in Window(_variables.Count, cursor))
        {
            var variable = _variables[index];
            var name = variable.Name.Length > nameWidth ? variable.Name.Cut(nameWidth) : variable.Name;
            body.Add(new($"{(index == cursor ? "▶" : " ")} {name.PadRight(nameWidth)} = {variable.Value}", index == cursor ? Color.Yellow : null));
        }
    }

    private void BuildTimeZones(List<InfoLines.Line> body)
    {
        var cursor = _cursors[(int)SettingsSection.TimeZone];
        foreach (var index in Window(_zones.Count, cursor))
        {
            var zone = _zones[index];
            var current = string.Equals(zone.Id, _currentZone, StringComparison.OrdinalIgnoreCase);
            body.Add(new($"{(index == cursor ? "▶" : " ")} {(current ? "●" : " ")} {zone.DisplayName}  [{zone.Id}]", index == cursor ? Color.Yellow : null));
        }
    }

    /// <summary>The indexes of the rows to draw so that <paramref name="cursor"/> is on screen, roughly centred.</summary>
    private IEnumerable<int> Window(int count, int cursor)
    {
        var rows = ContentRows;
        var first = Math.Clamp(cursor - rows / 2, 0, Math.Max(0, count - rows));
        return Enumerable.Range(first, Math.Max(0, Math.Min(rows, count - first)));
    }

    private string FooterHints()
    {
        if (_inputActive)
        {
            return " Enter:ok  Esc:cancel ";
        }

        if (_confirmPrompt is not null)
        {
            return " y:yes  n:no ";
        }

        return _section switch
        {
            SettingsSection.Computer => " ←/→:section  Enter:edit  F5:reread  Esc:close ",
            SettingsSection.Environment => " ←/→:section  Enter:edit  Ins:add  Del:remove  S:scope  Esc:close ",
            _ => " ←/→:section  Enter:apply  F5:reread  Esc:close ",
        };
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var footer = FooterHints().FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }

    // ── Mouse ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The wheel moves the highlight three rows; a click on a row highlights it, and a click on a section's name in
    /// the strip at the top opens that section. Nothing answers while a value is being typed, a question is up or a
    /// change is under way.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (!_inputActive && _confirmPrompt is null && !_busy && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            if (mouseEvent.WheelDelta != 0)
            {
                MoveCursor(mouseEvent.WheelDelta > 0 ? -3 : 3);
                return true;
            }

            if (mouseEvent.LeftClicked)
            {
                long row = mouseEvent.Position.Y - (AbsolutePosition.Y + 1);
                long column = mouseEvent.Position.X - (AbsolutePosition.X + 1);
                if (row == 0 && SectionAt(column) is { } section)
                {
                    SwitchSection(section - (int)_section);
                    return true;
                }

                if (row >= HeaderRows && row < HeaderRows + ContentRows && ItemAt((int)row - HeaderRows) is { } index)
                {
                    _cursors[(int)_section] = index;
                    Render();
                    return true;
                }
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>The section whose name the strip shows at <paramref name="column"/>: each is drawn as " Name " (or "[Name]"), two spaces apart.</summary>
    private static int? SectionAt(long column)
    {
        long start = 0;
        for (var i = 0; i < SectionNames.Length; i++)
        {
            long width = SectionNames[i].Length + 2;
            if (column >= start && column < start + width)
            {
                return i;
            }

            start += width + 2;
        }

        return null;
    }

    /// <summary>The item drawn on body row <paramref name="bodyRow"/> of the current section, or null for an empty row.</summary>
    private int? ItemAt(int bodyRow)
    {
        var count = ItemCount;
        int first = _section == SettingsSection.Computer
            ? 0
            : Math.Clamp(_cursors[(int)_section] - ContentRows / 2, 0, Math.Max(0, count - ContentRows));
        int index = first + bodyRow;
        return index >= 0 && index < count ? index : null;
    }

    // ── Keys ────────────────────────────────────────────────────────────────

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_confirmPrompt is not null)
        {
            yield return ("Y", iConsole.Localization.UiText.Get("Common.Confirm", "Confirm"));
            yield return ("N / Esc", iConsole.Localization.UiText.Get("Common.Cancel", "Cancel"));
            yield break;
        }

        if (_inputActive)
        {
            yield return ("Enter", "Apply the value");
            yield return ("Esc", "Cancel the entry");
            yield break;
        }

        yield return ("Left/Right, Tab", "Switch section");
        yield return ("Up/Down", "Move over the settings");
        yield return ("PageUp/PageDown", "Move a page at a time");
        yield return ("Home/End", "First or last setting");
        yield return ("Enter/F4", "Change the setting under the cursor");
        yield return ("F5", "Read the settings again");
        if (_section == SettingsSection.Environment)
        {
            yield return ("Insert/N", "Add a variable");
            yield return ("Delete/F8", "Remove the variable (asks first)");
            yield return ("S", "Switch between the user and the machine scope");
        }

        yield return ("Esc", iConsole.Localization.UiText.Get("Common.Close", "Close"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        if (_confirmPrompt is not null)
        {
            return HandleConfirmKey(keyEvent);
        }

        if (_inputActive)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Enter:
                    SubmitInput();
                    return true;
                case ConsoleKey.Escape:
                    CancelInput();
                    return true;
            }

            return await base.HandleKeyEventAsync(keyEvent);
        }

        if (keyEvent.Alt || keyEvent.Control)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            Close();
            return true;
        }

        if (_busy)
        {
            return true; // One change at a time; the answer is on its way.
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.RightArrow:
                SwitchSection(1);
                return true;
            case ConsoleKey.LeftArrow:
                SwitchSection(-1);
                return true;
            case ConsoleKey.Tab:
                SwitchSection(keyEvent.Shift ? -1 : 1);
                return true;
            case ConsoleKey.UpArrow:
                MoveCursor(-1);
                return true;
            case ConsoleKey.DownArrow:
                MoveCursor(1);
                return true;
            case ConsoleKey.PageUp:
                MoveCursor(-ContentRows);
                return true;
            case ConsoleKey.PageDown:
                MoveCursor(ContentRows);
                return true;
            case ConsoleKey.Home:
                MoveCursor(int.MinValue / 2);
                return true;
            case ConsoleKey.End:
                MoveCursor(int.MaxValue / 2);
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.F4:
                Activate();
                return true;
            case ConsoleKey.F5:
                Reread();
                return true;
        }

        if (_section == SettingsSection.Environment && !keyEvent.Shift)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Insert:
                case ConsoleKey.N:
                    BeginAddVariable();
                    return true;
                case ConsoleKey.Delete:
                case ConsoleKey.F8:
                    BeginRemoveVariable();
                    return true;
                case ConsoleKey.S:
                    ToggleScope();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private int ItemCount => _section switch
    {
        SettingsSection.Computer => 3,
        SettingsSection.Environment => _variables.Count,
        _ => _zones.Count,
    };

    private void SwitchSection(int step)
    {
        _section = (SettingsSection)(((int)_section + step + SectionNames.Length) % SectionNames.Length);
        if (_section == SettingsSection.TimeZone)
        {
            // Start where the system is, so Enter on a fresh tab does not move the clock by accident.
            var current = _zones.ToList().FindIndex(z => string.Equals(z.Id, _currentZone, StringComparison.OrdinalIgnoreCase));
            if (current >= 0 && _cursors[(int)_section] == 0)
            {
                _cursors[(int)_section] = current;
            }
        }

        _message = string.Empty;
        Render();
    }

    private void MoveCursor(int delta)
    {
        var count = ItemCount;
        if (count == 0)
        {
            return;
        }

        var index = (int)_section;
        _cursors[index] = (int)Math.Clamp((long)_cursors[index] + delta, 0, count - 1);
        Render();
    }

    private void Reread()
    {
        _message = string.Empty;
        PendingOperation = Task.Run(async () =>
        {
            _currentZone = _service.CurrentTimeZoneId;
            _identity = await _service.GetComputerAsync().ConfigureAwait(false);
            await LoadEnvironmentAsync().ConfigureAwait(false);
            Render();
        });
    }

    private void Activate()
    {
        switch (_section)
        {
            case SettingsSection.Computer:
                BeginEditComputer();
                break;
            case SettingsSection.Environment:
                BeginEditVariable();
                break;
            default:
                ApplyTimeZone();
                break;
        }
    }

    // ── Computer ────────────────────────────────────────────────────────────

    private void BeginEditComputer()
    {
        if (_identity is not { } identity)
        {
            return;
        }

        switch (_cursors[0])
        {
            case 0:
                ShowInput("Computer name", identity.ComputerName, name => Run(() => _service.SetComputerNameAsync(name)));
                break;
            case 1 when identity.CanChangeMembership:
                ShowInput("Workgroup", identity.Membership, name => Run(() => _service.SetMembershipAsync(name)));
                break;
            case 2 when identity.CanChangeDescription:
                ShowInput("Description", identity.Description, text => Run(() => _service.SetDescriptionAsync(text)));
                break;
            default:
                SetMessage(identity.IsDomainMember
                    ? $"This computer belongs to the domain {identity.Membership}; leaving it needs a directory account."
                    : "This setting cannot be changed on this system.", error: false);
                break;
        }
    }

    // ── Environment ─────────────────────────────────────────────────────────

    private SystemEnvironmentVariable? SelectedVariable =>
        _variablesLoaded && _cursors[(int)SettingsSection.Environment] is var i && i >= 0 && i < _variables.Count ? _variables[i] : null;

    private void ToggleScope()
    {
        _scope = _scope == EnvironmentScope.User ? EnvironmentScope.Machine : EnvironmentScope.User;
        _variables = [];
        _variablesLoaded = false;
        _cursors[(int)SettingsSection.Environment] = 0;
        _message = string.Empty;
        Render();
        PendingOperation = LoadEnvironmentAsync();
    }

    private void BeginAddVariable()
    {
        if (!_service.SupportsEnvironment(_scope))
        {
            return;
        }

        ShowInput("New variable name", string.Empty, name =>
        {
            name = name.Trim();
            var windows = OperatingSystem.IsWindows();
            if (SystemSettingsValidator.ValidateVariableName(name, windows) is { } problem)
            {
                SetMessage(problem, error: true);
                return Task.CompletedTask;
            }

            ShowInput($"{name} =", string.Empty, value => Run(() => _service.SetEnvironmentVariableAsync(_scope, name, value)));
            return Task.CompletedTask;
        });
    }

    private void BeginEditVariable()
    {
        if (SelectedVariable is not { } variable)
        {
            return;
        }

        ShowInput($"{variable.Name} =", variable.Value, value => Run(() => _service.SetEnvironmentVariableAsync(_scope, variable.Name, value)));
    }

    private void BeginRemoveVariable()
    {
        if (SelectedVariable is not { } variable)
        {
            return;
        }

        var scope = _scope;
        Confirm($"Remove {variable.Name}?", () => Run(() => _service.RemoveEnvironmentVariableAsync(scope, variable.Name)));
    }

    // ── Time zone ───────────────────────────────────────────────────────────

    private void ApplyTimeZone()
    {
        var index = _cursors[(int)SettingsSection.TimeZone];
        if (index < 0 || index >= _zones.Count)
        {
            return;
        }

        var zone = _zones[index];
        if (string.Equals(zone.Id, _currentZone, StringComparison.OrdinalIgnoreCase))
        {
            SetMessage("That is the current time zone.", error: false);
            return;
        }

        Run(() => _service.SetTimeZoneAsync(zone.Id));
    }

    // ── Changes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a change on a task and shows its answer; whatever it changed is then read again, so the
    /// rows show what the system now holds and not what was typed.
    /// </summary>
    private Task Run(Func<Task<SystemSettingResult>> change)
    {
        _busy = true;
        Render();
        PendingOperation = Task.Run(async () =>
        {
            SystemSettingResult result;
            try
            {
                result = await change().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = SystemSettingResult.Fail(exception.Message);
            }

            _busy = false;
            _message = result.Message;
            _messageIsError = !result.Success;
            if (result.Success)
            {
                _currentZone = _service.CurrentTimeZoneId;
                _identity = await _service.GetComputerAsync().ConfigureAwait(false);
                await LoadEnvironmentAsync().ConfigureAwait(false);
            }

            Render();
        });
        return PendingOperation;
    }

    private void SetMessage(string message, bool error)
    {
        _message = message;
        _messageIsError = error;
        Render();
    }

    // ── Input and confirmation ──────────────────────────────────────────────

    private void ShowInput(string label, string initialText, Func<string, Task> onSubmit)
    {
        _onSubmit = onSubmit;
        _input.Label = label;
        _input.Text = initialText;
        _input.ScrollableModel.CursorPosition.X = initialText.Length;
        _inputActive = true;
        ApplyInputVisibility();
        _input.SetFocus();
        StateHasChanged();
    }

    private void ApplyInputVisibility()
    {
        _input.CanFocus = _inputActive;
        _input.IsVisible = _inputActive;
        _input.IsEnabled = _inputActive;
    }

    private void SubmitInput()
    {
        var text = _input.Text;
        var callback = _onSubmit;
        HideInput();
        callback?.Invoke(text);
    }

    private void CancelInput()
    {
        HideInput();
        SetMessage("Cancelled.", error: false);
    }

    private void HideInput()
    {
        _input.ResetFocus();
        _inputActive = false;
        ApplyInputVisibility();
        _input.Text = string.Empty;
        _onSubmit = null;
        StateHasChanged();
    }

    private void Confirm(string prompt, Func<Task> action)
    {
        _confirmPrompt = prompt;
        _onConfirm = action;
        Render();
    }

    /// <summary>y confirms; n and Esc cancel; any other key is no answer either way when the question is "remove this".</summary>
    private bool HandleConfirmKey(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Y)
        {
            var action = _onConfirm;
            _confirmPrompt = null;
            _onConfirm = null;
            action?.Invoke();
        }
        else if (keyEvent.ConsoleKey is ConsoleKey.N or ConsoleKey.Escape)
        {
            _confirmPrompt = null;
            _onConfirm = null;
            SetMessage("Cancelled.", error: false);
        }

        return true;
    }
}
