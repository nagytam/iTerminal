using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services.Updates;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Offers a newer release of iTerminal. On Windows Enter downloads the installer and starts it; elsewhere the
/// dialog only says where the release is. S skips that release, Esc asks again at the next start.
/// </summary>
public class UpdateDialog : Dialog
{
    private const int PreferredWidth = 74;
    private const int LogRows = 3;
    private const int Rows = 9; // Borders, three text rows, blanks, the log and the hint.

    private readonly IUpdateService _service;

    public UpdateDialog(IUpdateService service, IThemeService themeService)
        : base("Update", themeService)
    {
        _service = service;
        Title = "Update available";
    }

    /// <summary>Called once the installer is running: whoever owns the application closes it so the installer can replace its files.</summary>
    public Action? OnInstallerStarted { get; set; }

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
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && plain)
        {
            if (_service.IsBusy)
            {
                return true;
            }

            if (_service.CanInstall && _service.Available is not null)
            {
                _ = Task.Run(InstallAsync);
            }
            else
            {
                Close();
            }

            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.S && plain && !_service.IsBusy)
        {
            _service.Skip();
            Close();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
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
                ? _service.Available?.Installer is null ? "This release has no Windows installer yet." : "Enter downloads the installer and runs it; iTerminal closes while it does."
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

    internal string Hint => _service.IsBusy
        ? "Working..."
        : _service.CanInstall && _service.Available?.Installer is not null
            ? "Enter: install   S: skip this version   Esc: later"
            : "S: skip this version   Esc: later";

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
        for (int i = 0; i < rows.Count && i + 1 < ViewSize.Height - 2; i++)
        {
            WriteRow(buffer, colorScheme, innerWidth, i + 1, rows[i].Text, rows[i].Color);
        }

        string hint = Hint.Length <= innerWidth ? Hint : Hint.Cut(innerWidth);
        WriteRow(buffer, colorScheme, innerWidth, (int)ViewSize.Height - 2, hint, colorScheme.DisabledForeground);
    }

    private void WriteRow(IBuffer buffer, ColorScheme colorScheme, int innerWidth, int row, string text, Color color)
    {
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + row),
            new Size(innerWidth, 1),
            new ConxelString(text.ExpandRight(innerWidth), color, colorScheme.Background));
    }
}
