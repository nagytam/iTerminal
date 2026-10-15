using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The components iTerminal downloads rather than ships, and the way to install or update them. It opens
/// at start-up when something is missing or newer than what is installed, and from Tools at any time.
/// Each dependency takes three rows — its name, its state, and a note or problem — so the dialog keeps
/// the same size while an install runs and the list changes under it.
/// </summary>
public class RuntimeDependencyDialog : Dialog
{
    private const int PreferredWidth = 78;
    private const int LogRows = 3;
    private const int RowsPerDependency = 3;
    private const int FixedRows = 10; // Borders, intro, blanks, the log and the hint.

    private readonly IRuntimeDependencyService _service;

    public RuntimeDependencyDialog(IRuntimeDependencyService service, IThemeService themeService)
        : base("Runtime dependencies", themeService)
    {
        _service = service;
        Title = "Runtime dependencies";
    }

    /// <summary>Whether anything is left to install or update.</summary>
    internal bool HasWork => _service.Statuses.Any(status => status.State != RuntimeDependencyState.Installed);

    /// <summary>Whether the start-up should open this dialog: something is missing, or newer, and not skipped.</summary>
    public bool ShouldOpenAtStartup => _service.ShouldOfferAtStartup;

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            int width = (int)Math.Max(20, Math.Min(PreferredWidth, (long)Parent.ViewSize.Width));
            int height = (int)Math.Max(FixedRows, Math.Min(FixedRows + RowsPerDependency * _service.Statuses.Count, (long)Parent.ViewSize.Height));
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

            if (!HasWork)
            {
                Close();
                return true;
            }

            _service.StartInstall();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.S && plain && HasWork && !_service.IsBusy)
        {
            _service.SkipOffers();
            Close();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// The rows of the dialog, each with its colour. Built from the service's current snapshot on every draw,
    /// so a status or a log line that changed in the background shows on the next idle repaint.
    /// </summary>
    internal List<(string Text, Color Color)> Rows(int innerWidth, ColorScheme scheme)
    {
        List<(string Text, Color Color)> rows =
        [
            ("Components iTerminal downloads into your data folder, rather than shipping them.", scheme.Foreground),
            (string.Empty, scheme.Foreground),
        ];

        foreach (RuntimeDependencyStatus status in _service.Statuses)
        {
            rows.Add(($"{status.Name} - {status.Purpose}", scheme.Foreground));
            rows.Add((StateText(status), status.State == RuntimeDependencyState.Installed ? scheme.Foreground : Color.DarkYellow));
            rows.Add(status.Problem is not null
                ? (status.Problem, Color.DarkRed)
                : (status.Note ?? string.Empty, scheme.DisabledForeground));
        }

        rows.Add((string.Empty, scheme.Foreground));
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

    private static string StateText(RuntimeDependencyStatus status) => status.State switch
    {
        RuntimeDependencyState.Missing => $"Not installed. Install {status.TargetVersion}.",
        RuntimeDependencyState.UpdateAvailable => $"Installed {status.InstalledVersion}. Update to {status.TargetVersion}.",
        _ => $"Installed {status.InstalledVersion}. Up to date.",
    };

    internal string Hint => _service.IsBusy
        ? "Installing... Esc: hide this (it keeps going)"
        : HasWork
            ? "Enter: install   S: skip this version   Esc: later"
            : "Enter or Esc: close";

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        int innerWidth = (int)ViewSize.Width - 2;
        if (innerWidth <= 0 || ViewSize.Height < 3)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        List<(string Text, Color Color)> rows = Rows(innerWidth, colorScheme);
        // Stops above the hint row: on a screen too short for every row, the last ones are cut, not the hint.
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
