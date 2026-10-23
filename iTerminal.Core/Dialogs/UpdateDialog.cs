using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services.Updates;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Offers a newer release of iTerminal. On Windows the Install updates button, focused when the dialog opens,
/// downloads the installer and starts it; elsewhere the dialog only says where the release is. S skips that
/// release, Esc asks again at the next start.
/// </summary>
public class UpdateDialog : FormDialog
{
    private const int PreferredWidth = 74;
    private const int LogRows = 3;
    private const int SkipButtonWidth = 21;
    private const int Rows = 10; // Borders, three text rows, blanks, the log, the hint and the buttons.

    private readonly IUpdateService _service;
    private readonly Button _installButton;
    private readonly Button _skipButton;
    private readonly Button _cancelButton;

    public UpdateDialog(IUpdateService service, IThemeService themeService)
        : base("Update", themeService)
    {
        _service = service;
        Title = "Update available";

        var installCommand = new InstallCommand();
        AddCommand(installCommand);
        _installButton = new Button("Install updates", installCommand, themeService);
        var skipCommand = new SkipCommand();
        AddCommand(skipCommand);
        _skipButton = new Button("Skip this version", skipCommand, themeService) { HotKeyChar = 'S' };
        _cancelButton = new CancelDialogButton(themeService);
        AddControl(_installButton);
        AddControl(_skipButton);
        AddControl(_cancelButton);
    }

    /// <summary>Called once the installer is running: whoever owns the application closes it so the installer can replace its files.</summary>
    public Action? OnInstallerStarted { get; set; }

    private int _shownLogCount = -1;
    private bool _shownBusy;

    /// <summary>
    /// Redraws when the service has something new to say. The download runs on a worker and the dialog only reads the
    /// service when it draws, so without this the log and "Working..." stood still until a key was pressed.
    /// </summary>
    public override void OnIdle()
    {
        base.OnIdle();
        int logCount = _service.Log.Count;
        bool busy = _service.IsBusy;
        if (logCount != _shownLogCount || busy != _shownBusy)
        {
            _shownLogCount = logCount;
            _shownBusy = busy;
            StateHasChanged();
        }
    }

    /// <summary>Whether the dialog has anything to offer: a newer release the person did not skip.</summary>
    public bool ShouldOpen => _service.Available is not null && !_service.IsSkipped;

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            int width = (int)Math.Max(20, Math.Min(PreferredWidth, (long)Parent.ViewSize.Width));
            int height = (int)Math.Max(5, Math.Min(Rows, (long)Parent.ViewSize.Height));
            CenterInParent(new Size(width, height));
        }

        base.OnInitializeLayout();
        LayoutButtonRow(_installButton, _cancelButton);

        // Between the other two, as far from both as the row allows.
        long gapStart = _installButton.Position.X + _installButton.ViewSize.Width;
        long skipWidth = Math.Min(SkipButtonWidth, Math.Max(1, _cancelButton.Position.X - gapStart));
        long skipX = gapStart + Math.Max(0, (_cancelButton.Position.X - gapStart - skipWidth) / 2);
        _skipButton.Position = new Position((int)skipX, _installButton.Position.Y);
        _skipButton.ViewSize = new Size((int)skipWidth, 1);
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && plain)
        {
            Install();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.S && plain)
        {
            Skip();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>Enter, or the Install updates button: downloads and starts the installer, or just closes when there is none to run.</summary>
    private void Install()
    {
        if (_service.IsBusy)
        {
            return;
        }

        if (_service.CanInstall && _service.Available is not null)
        {
            _ = Task.Run(InstallAsync);
        }
        else
        {
            Close();
        }
    }

    /// <summary>S, or the Skip button: stops offering this release until a newer one appears.</summary>
    private void Skip()
    {
        if (_service.IsBusy)
        {
            return;
        }

        _service.Skip();
        Close();
    }

    private async Task InstallAsync()
    {
        if (await _service.InstallAsync(CancellationToken.None))
        {
            OnInstallerStarted?.Invoke();
        }
    }

    internal List<(string Text, Color Color)> TextRows(int innerWidth, ColorScheme scheme)
    {
        string latest = _service.Available?.Version ?? "?";
        List<(string Text, Color Color)> rows =
        [
            ($"iTerminal {latest} is available. You have {_service.CurrentVersion}.", scheme.Foreground),
            (_service.CanInstall
                ? _service.Available?.Installer is null ? "This release has no Windows installer yet." : "iTerminal closes while the installer runs."
                : $"Download it from {_service.ReleasesAddress}", scheme.DisabledForeground),
            (string.Empty, scheme.Foreground),
        ];

        IReadOnlyList<string> log = _service.Log;
        for (int i = Math.Max(0, log.Count - LogRows); i < log.Count; i++)
        {
            rows.Add((log[i], scheme.DisabledForeground));
        }

        for (int i = log.Count; i < LogRows; i++)
        {
            rows.Add((string.Empty, scheme.DisabledForeground));
        }

        return rows.Select(row => (row.Text.Length > innerWidth ? row.Text.Cut(innerWidth) : row.Text, row.Color)).ToList();
    }

    internal string Hint => _service.IsBusy ? "Working..." : string.Empty;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        int innerWidth = (int)ViewSize.Width - 2;
        if (innerWidth <= 0 || ViewSize.Height < 3)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        List<(string Text, Color Color)> rows = TextRows(innerWidth, colorScheme);
        for (int i = 0; i < rows.Count && i + 1 < ViewSize.Height - 3; i++)
        {
            WriteRow(buffer, colorScheme, innerWidth, i + 1, rows[i].Text, rows[i].Color);
        }

        string hint = Hint.Length <= innerWidth ? Hint : Hint.Cut(innerWidth);
        WriteRow(buffer, colorScheme, innerWidth, (int)ViewSize.Height - 3, hint, colorScheme.DisabledForeground);
    }

    private void WriteRow(IBuffer buffer, ColorScheme colorScheme, int innerWidth, int row, string text, Color color)
    {
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + row),
            new Size(innerWidth, 1),
            new ConxelString(text.ExpandRight(innerWidth), color, colorScheme.Background));
    }

    private sealed class InstallCommand : Command
    {
        public override string Title => "Install updates";
        public override string Description => "Download the new release and run its installer.";

        public override Task ExecuteAsync(object? args)
        {
            var dialog = Control as UpdateDialog ?? Control?.Parent as UpdateDialog;
            dialog?.Install();
            return Task.CompletedTask;
        }
    }

    private sealed class SkipCommand : Command
    {
        public override string Title => "Skip this version";
        public override string Description => "Do not offer this release again.";

        public override Task ExecuteAsync(object? args)
        {
            var dialog = Control as UpdateDialog ?? Control?.Parent as UpdateDialog;
            dialog?.Skip();
            return Task.CompletedTask;
        }
    }
}
