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
public partial class UpdateDialog : FormDialog
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

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", "Install the update");
        yield return ("S", "Skip this version");
        yield return ("Esc", "Close and ask again later");
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
        if (_service.IsBusy && ParseProgress(log) is { } progress)
        {
            // The download's own words are the console output; what the person wants from them is how far it is.
            (string filled, string empty, string tail) = ProgressBar(progress, innerWidth);
            rows.Add((progress.Stage, scheme.Foreground));
            rows.Add((filled + empty + tail, Color.Green));
            rows.Add((string.Empty, scheme.DisabledForeground));
        }
        else
        {
            for (int i = Math.Max(0, log.Count - LogRows); i < log.Count; i++)
            {
                rows.Add((log[i], scheme.DisabledForeground));
            }

            for (int i = log.Count; i < LogRows; i++)
            {
                rows.Add((string.Empty, scheme.DisabledForeground));
            }
        }

        return rows.Select(row => (row.Text.Length > innerWidth ? row.Text.Cut(innerWidth) : row.Text, row.Color)).ToList();
    }

    /// <summary>"Working..." while something runs and nothing can be shown as progress yet; the bar says it all otherwise.</summary>
    internal string Hint => _service.IsBusy && ParseProgress(_service.Log) is null ? "Working..." : string.Empty;

    /// <summary>How far the download is, read from the service's log: a fraction from 0 to 1 and the words that go beside the bar.</summary>
    internal readonly record struct UpdateProgress(double Fraction, string Label, string Stage = "");

    /// <summary>
    /// The newest progress the log reports. The download says "Downloading name: 12.3 of 29.2 MB" as it goes, then
    /// "Downloaded name (29.2 MB, checksum verified)", then that the installer is starting; the last two mean it is
    /// all there. A download of unknown size, or no download yet, has no fraction: null.
    /// </summary>
    internal static UpdateProgress? ParseProgress(IReadOnlyList<string> log)
    {
        for (int i = log.Count - 1; i >= 0; i--)
        {
            string line = log[i];
            if (line.StartsWith("Starting the installer", StringComparison.Ordinal) || line.StartsWith("Downloaded ", StringComparison.Ordinal))
            {
                return new UpdateProgress(1, "download complete", line.StartsWith("Starting", StringComparison.Ordinal) ? "Starting the installer" : "Download complete");
            }

            System.Text.RegularExpressions.Match match = DownloadLine().Match(line);
            if (match.Success
                && double.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double received)
                && double.TryParse(match.Groups[2].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double total)
                && total > 0)
            {
                return new UpdateProgress(Math.Clamp(received / total, 0, 1), $"{match.Groups[1].Value} of {match.Groups[2].Value} MB", line[..line.LastIndexOf(": ", StringComparison.Ordinal)]);
            }

            if (line.StartsWith("The update could not be installed", StringComparison.Ordinal))
            {
                return null;
            }
        }

        return null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^Downloading .*: ([0-9][0-9.,]*) of ([0-9][0-9.,]*) MB$")]
    private static partial System.Text.RegularExpressions.Regex DownloadLine();

    /// <summary>The bar row: <c>[██████░░░░]  42%  12.3 of 29.2 MB</c>, cut to fit <paramref name="width"/>.</summary>
    internal static (string Filled, string Empty, string Tail) ProgressBar(UpdateProgress progress, int width)
    {
        string tail = $"  {(int)Math.Round(progress.Fraction * 100),3}%  {progress.Label}";
        int barWidth = Math.Clamp(width - tail.Length - 2, 0, 30);
        if (barWidth < 4)
        {
            return (string.Empty, string.Empty, $"{(int)Math.Round(progress.Fraction * 100)}%".Cut(width));
        }

        int filled = (int)Math.Round(progress.Fraction * barWidth);
        return ("[" + new string('█', filled), new string('░', barWidth - filled) + "]", tail);
    }

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

        int hintRow = (int)ViewSize.Height - 3;
        string hint = Hint.Length <= innerWidth ? Hint : Hint.Cut(innerWidth);
        WriteRow(buffer, colorScheme, innerWidth, hintRow, hint, colorScheme.DisabledForeground);
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
