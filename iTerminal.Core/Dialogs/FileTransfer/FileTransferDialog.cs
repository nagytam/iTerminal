using System.Text;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Qr;
using iConsole.Services;
using iTerminal.Core.Services.Sharing;

namespace iTerminal.Core.Dialogs.FileTransfer;

/// <summary>
/// File transfer: serves the selected local files over HTTPS on the LAN and shows the address as a QR code and as text,
/// so a phone can download them. The share runs while the dialog is open; Esc or closing the dialog stops it.
///
/// <para>Starting, stopping and the encoding happen on background tasks. <see cref="OnIdle"/> reads the service's state
/// snapshot and updates the labels and the code; nothing here waits on the server. The rows are planned by
/// <see cref="FileTransferLayout"/>, so a short terminal keeps the code (in a compact form) before it drops it.</para>
/// </summary>
public class FileTransferDialog : Dialog
{
    private const int MaxTextLines = 3;
    private const int DefaultTextWidth = 76;

    private readonly IFileShareService _service;
    private readonly Label _selection;
    private readonly Label[] _url = new Label[MaxTextLines];
    private readonly QrCodeView _code;
    private readonly Label[] _warning = new Label[MaxTextLines];
    private readonly Label _status;

    private FileShareState? _shown;
    private string? _failureKey;
    private volatile string? _startFailureKey;
    private volatile bool _beginning;
    private int _innerWidth;
    private int _innerHeight;

    public FileTransferDialog(IFileShareService service, IThemeService themeService)
        : base(nameof(FileTransferDialog), themeService)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Title = UiText.Get("FileTransfer.Title", "File transfer");

        _selection = new Label(string.Empty, themeService);
        _code = new QrCodeView(themeService);
        _status = new Label(string.Empty, themeService);
        AddControl(_selection);
        for (int i = 0; i < MaxTextLines; i++)
        {
            _url[i] = new Label(string.Empty, themeService);
            AddControl(_url[i]);
        }

        AddControl(_code);
        for (int i = 0; i < MaxTextLines; i++)
        {
            _warning[i] = new Label(string.Empty, themeService);
            AddControl(_warning[i]);
        }

        AddControl(_status);
    }

    #region Test seams

    internal string SelectionText => _selection.Text;
    internal string UrlText => string.Concat(_url.Select(label => label.Text));
    internal string StatusText => _status.Text;
    internal bool CodeIsShown => _code.IsVisible;
    internal QrCodeView CodeView => _code;
    internal IReadOnlyList<string> WarningText => _warning.Select(label => label.Text).ToList();

    #endregion

    /// <summary>
    /// Hands the dialog the selection the command captured, and starts the share. Items that are not local files are
    /// refused with a reason, and nothing is started.
    /// </summary>
    public void Begin(IReadOnlyList<Url> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        _selection.Text = UiText.Get("FileTransfer.Selection", "Selected: {0}").Replace("{0}", selection.Count.ToString(), StringComparison.Ordinal);

        if (selection.Count == 0)
        {
            ShowFailure("FileTransfer.NoSelection", "Select a file or folder first.");
            return;
        }

        if (selection.Any(url => url.ContainerType != ContainerType.FileSystem))
        {
            ShowFailure("FileTransfer.DriveNotShareable", "Only files on this computer can be shared.");
            return;
        }

        _failureKey = null;
        _startFailureKey = null;
        _beginning = true;
        _status.Text = UiText.Get("FileTransfer.Starting", "Starting…");
        IReadOnlyList<string> paths = selection.Select(url => url.Path).ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                FileShareStartResult result = await _service.StartAsync(paths);
                if (!result.Started)
                {
                    // A refusal that leaves the state alone (busy) is shown from the idle pass rather than lost here.
                    _startFailureKey = result.FailureKey;
                }
            }
            catch (Exception)
            {
                _startFailureKey = "FileTransfer.BindFailed";
            }
        });
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            // A short terminal gives the dialog all of its height: the code needs every row it can get.
            double share = Parent.ViewSize.Height < 40 ? 1.0 : 0.8;
            CenterInParent(Parent.ViewSize * share);
        }

        _innerWidth = (int)ViewSize.Width - 2;
        _innerHeight = (int)ViewSize.Height - 2;
        Relayout();
        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Esc", UiText.Get("FileTransfer.Stop", "Stop the share and close"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down && keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            Close();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void OnIdle()
    {
        base.OnIdle();
        if (_failureKey is not null)
        {
            return;
        }

        string? startFailure = _startFailureKey;
        FileShareState state = _service.State ?? FileShareState.Stopped;
        if (_shown is null || !SameShown(_shown, state))
        {
            _shown = state;
            Relayout();
        }

        if (startFailure is not null)
        {
            _beginning = false;
            _status.Text = Reason(startFailure);
        }
        else if (_beginning && state.Status == FileShareStatus.Stopped)
        {
            // The service has not published its start yet: keep "Starting…" rather than flash "Stopped".
            _status.Text = UiText.Get("FileTransfer.Starting", "Starting…");
        }
        else
        {
            _beginning = false;
            _status.Text = StatusFor(state);
        }
    }

    public override void Close()
    {
        if (_failureKey is null)
        {
            _ = Task.Run(() => _service.StopAsync());
        }

        base.Close();
    }

    private static string StatusFor(FileShareState state) => state.Status switch
    {
        FileShareStatus.Running => UiText.Get("FileTransfer.Requests", "Requests served: {0}").Replace("{0}", state.Requests.ToString(), StringComparison.Ordinal),
        FileShareStatus.Starting => UiText.Get("FileTransfer.Starting", "Starting…"),
        FileShareStatus.Failed => Reason(state.Message),
        FileShareStatus.Expired => UiText.Get("FileTransfer.Expired", "The share has expired. Close the dialog and open File transfer again for a new address."),
        _ => UiText.Get("FileTransfer.Stopped", "Stopped"),
    };

    private void ShowFailure(string key, string english)
    {
        _failureKey = key;
        _shown = null;
        foreach (Label label in _url)
        {
            label.Text = string.Empty;
        }

        _code.SetModules(null, QrTextRenderer.DefaultQuietZone);
        _status.Text = UiText.Get(key, english);
        Relayout();
    }

    /// <summary>Puts every label where the layout plan says, from the current state, the URL, the code and the warning.</summary>
    private void Relayout()
    {
        FileShareState state = _service.State ?? FileShareState.Stopped;
        string url = state.Url ?? string.Empty;
        // Before the first layout pass the width is still zero: one column is the least any text can use.
        // Before the first layout pass there is no width yet: text then gets a standard terminal width, not one column.
        int textWidth = _innerWidth > 2 ? _innerWidth - 2 : DefaultTextWidth;
        IReadOnlyList<string> urlLines = FileTransferLayout.WrapText(url, textWidth, MaxTextLines, breakAnywhere: true);
        IReadOnlyList<string> warningLines = FileTransferLayout.WrapText(UiText.Get("FileTransfer.CertificateWarning", "The phone shows a certificate warning: accept it to download. Anyone with this address can read the selected files while this dialog is open."), textWidth, MaxTextLines, breakAnywhere: false);

        if (state.QrModules is null)
        {
            _code.SetModules(null, QrTextRenderer.DefaultQuietZone);
        }

        int codeModules = state.QrModules is { } modules ? modules.GetLength(0) : 0;
        FileTransferLayoutPlan plan = FileTransferLayout.Plan(_innerHeight, urlLines.Count, codeModules, warningLines.Count);

        _selection.Position = new Position(1, Math.Max(0, plan.SelectionRow) + 1);
        _selection.ViewSize = new Size(textWidth, 1);
        _selection.IsVisible = plan.SelectionRow >= 0;

        for (int i = 0; i < MaxTextLines; i++)
        {
            Label label = _url[i];
            label.Text = i < urlLines.Count ? urlLines[i] : string.Empty;
            label.Position = new Position(1, Math.Max(0, plan.UrlTop + i) + 1);
            label.ViewSize = new Size(textWidth, 1);
            label.IsVisible = i < urlLines.Count;
        }

        if (state.QrModules is { } drawn)
        {
            _code.SetModules(drawn, plan.QuietZone);
        }

        _code.Position = new Position(1, Math.Max(0, plan.CodeTop) + 1);
        _code.IsVisible = plan.ShowsCode && state.QrModules is not null && _code.ViewSize.Width <= textWidth;

        for (int i = 0; i < MaxTextLines; i++)
        {
            Label label = _warning[i];
            label.Text = i < plan.WarningRows && i < warningLines.Count ? warningLines[i] : string.Empty;
            label.Position = new Position(1, Math.Max(0, plan.WarningTop + i) + 1);
            label.ViewSize = new Size(textWidth, 1);
            label.IsVisible = i < plan.WarningRows && i < warningLines.Count;
        }

        _status.Position = new Position(1, Math.Max(0, plan.StatusRow) + 1);
        _status.ViewSize = new Size(textWidth, 1);
    }

    private static bool SameShown(FileShareState a, FileShareState b) =>
        a.Status == b.Status && a.Url == b.Url && a.Requests == b.Requests && a.Message == b.Message;

    private static string Reason(string? key) => key switch
    {
        "FileTransfer.BindFailed" => UiText.Get("FileTransfer.BindFailed", "No port of 8080 to 8089 is free, or no LAN address was found."),
        "FileTransfer.Busy" => UiText.Get("FileTransfer.Busy", "A share is already running."),
        "FileTransfer.NoSelection" => UiText.Get("FileTransfer.NoSelection", "Select a file or folder first."),
        "FileTransfer.ContainsAt" => UiText.Get("FileTransfer.ContainsAt", "This folder's path contains \"@/\" and cannot be shared."),
        _ => UiText.Get("FileTransfer.Stopped", "Stopped"),
    };
}

/// <summary>The rows of the dialog inside its border, from 0: where each part goes, and whether the code is shown and with which quiet zone.</summary>
internal sealed record FileTransferLayoutPlan(int SelectionRow, int UrlTop, int CodeTop, int QuietZone, int WarningTop, int WarningRows, int StatusRow)
{
    public bool ShowsCode => QuietZone > 0;
}

/// <summary>
/// Plans the rows of the file transfer dialog. The full layout comes first (selection line, a gap, the code with the
/// standard quiet zone of four modules, then the warning). When the terminal is too short, the layout gives up the
/// selection line and the gap, then the code's quiet zone (two modules), then warning lines, before it drops the code.
/// The URL text is always shown.
/// </summary>
internal static class FileTransferLayout
{
    /// <summary>Rows a code of <paramref name="modules"/> modules takes, two module rows to a line, with the given quiet zone.</summary>
    internal static int CodeRows(int modules, int quietZone) => (modules + 2 * quietZone + 1) / 2;

    public static FileTransferLayoutPlan Plan(int innerHeight, int urlRows, int codeModules, int warningRows)
    {
        int status = Math.Max(0, innerHeight - 1);
        int warning = Math.Clamp(warningRows, 0, MaxTextLines);
        urlRows = Math.Clamp(urlRows, 0, MaxTextLines);

        if (codeModules > 0)
        {
            foreach (bool compact in new[] { false, true })
            {
                for (int rows = warning; rows >= 1; rows--)
                {
                    int urlTop = compact ? 0 : 1;
                    int codeTop = urlTop + urlRows + (compact ? 0 : 1);
                    int warningTop = status - rows;
                    foreach (int quiet in new[] { 4, 2 })
                    {
                        if (codeTop + CodeRows(codeModules, quiet) <= warningTop)
                        {
                            return new FileTransferLayoutPlan(compact ? -1 : 0, urlTop, codeTop, quiet, warningTop, rows, status);
                        }
                    }
                }
            }
        }

        // No room for the code: the URL, then as much of the warning as fits, then the status line.
        int urlTopWithout = Math.Min(1, Math.Max(0, status));
        int available = Math.Max(0, status - (urlTopWithout + urlRows));
        int warningRowsWithout = Math.Min(warning, available);
        return new FileTransferLayoutPlan(0, urlTopWithout, urlTopWithout + urlRows, 0, status - warningRowsWithout, warningRowsWithout, status);
    }

    private const int MaxTextLines = 3;

    /// <summary>
    /// Wraps text into at most <paramref name="maxLines"/> lines no wider than <paramref name="width"/>. Words are kept whole
    /// where they fit; a word longer than a line, or any text when <paramref name="breakAnywhere"/> is set (a URL), is cut.
    /// </summary>
    internal static IReadOnlyList<string> WrapText(string text, int width, int maxLines, bool breakAnywhere)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        List<string> lines = [];
        if (breakAnywhere)
        {
            for (int start = 0; start < text.Length; start += width)
            {
                lines.Add(text.Substring(start, Math.Min(width, text.Length - start)));
            }
        }
        else
        {
            StringBuilder line = new();
            foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string piece = word;
                while (piece.Length > width)
                {
                    if (line.Length > 0)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                    }

                    lines.Add(piece[..width]);
                    piece = piece[width..];
                }

                if (line.Length > 0 && line.Length + 1 + piece.Length > width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }

                if (line.Length > 0)
                {
                    line.Append(' ');
                }

                line.Append(piece);
            }

            if (line.Length > 0)
            {
                lines.Add(line.ToString());
            }
        }

        return lines.Count <= maxLines ? lines : lines.Take(maxLines).ToList();
    }
}

/// <summary>
/// Paints a QR matrix as text in black on white, two module rows to a line, so a dark code reads as dark on any theme.
/// The lines are cached per matrix and quiet zone: an idle pass does not encode the same code again.
/// </summary>
internal sealed class QrCodeView : Control
{
    private IReadOnlyList<string> _lines = [];

    public QrCodeView(IThemeService themeService) : base(themeService)
    {
    }

    public bool[,]? LastModules { get; private set; }

    public int LastQuietZone { get; private set; } = QrTextRenderer.DefaultQuietZone;

    public void SetModules(bool[,]? modules, int quietZone)
    {
        if (ReferenceEquals(modules, LastModules) && quietZone == LastQuietZone)
        {
            return;
        }

        LastModules = modules;
        LastQuietZone = quietZone;
        _lines = modules is null ? [] : QrTextRenderer.Render(modules, quietZone);
        ViewSize = new Size(_lines.Count == 0 ? 0 : _lines[0].Length, _lines.Count);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        for (int row = 0; row < _lines.Count && row < ViewSize.Height; row++)
        {
            ConxelString line = new(_lines[row], Color.Black, Color.White, false, false, false, false);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(ViewSize.Width, 1), line);
        }
    }
}
