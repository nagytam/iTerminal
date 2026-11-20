using System.Text;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services.Sharing;

namespace iTerminal.Core.Dialogs.FileTransfer;

/// <summary>
/// File transfer: serves the selected local files over HTTPS on the LAN and shows the address as a QR code and as text,
/// so a phone can download them. The share runs while the dialog is open; Esc or closing the dialog stops it.
///
/// <para>Starting, stopping and the encoding happen on background tasks. <see cref="OnIdle"/> reads the service's state
/// snapshot and updates the labels and the code; nothing here waits on the server.</para>
/// </summary>
public class FileTransferDialog : Dialog
{
    /// <summary>The QR code is shown only when the dialog is at least this many rows tall.</summary>
    internal const int MinimumHeightForCode = 30;

    private const int WarningLines = 3;

    private readonly IFileShareService _service;
    private readonly Label _selection;
    private readonly Label _url;
    private readonly QrCodeView _code;
    private readonly Label _status;
    private readonly Label[] _warning = new Label[WarningLines];

    private FileShareState? _shown;
    private string? _failureKey;
    private volatile string? _startFailureKey;
    private int _innerWidth;
    private int _innerHeight;

    public FileTransferDialog(IFileShareService service, IThemeService themeService)
        : base(nameof(FileTransferDialog), themeService)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        Title = UiText.Get("FileTransfer.Title", "File transfer");

        _selection = new Label(string.Empty, themeService);
        _url = new Label(string.Empty, themeService);
        _code = new QrCodeView(themeService);
        _status = new Label(string.Empty, themeService);
        AddControl(_selection);
        AddControl(_url);
        AddControl(_code);
        for (int i = 0; i < WarningLines; i++)
        {
            _warning[i] = new Label(string.Empty, themeService);
            AddControl(_warning[i]);
        }

        AddControl(_status);
    }

    #region Test seams

    internal string SelectionText => _selection.Text;
    internal string UrlText => _url.Text;
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
            CenterInParent(Parent.ViewSize * 0.8);
        }

        int inner = (int)ViewSize.Width - 2;
        int height = (int)ViewSize.Height - 2;
        _innerWidth = inner;
        _innerHeight = height;
        if (inner > 0 && height > 0)
        {
            int width = Math.Max(1, inner - 2);
            _selection.Position = new Position(1, 1);
            _selection.ViewSize = new Size(width, 1);
            _url.Position = new Position(1, 2);
            _url.ViewSize = new Size(width, 1);
            _code.Position = new Position(1, 4);
            LayOutWarning(width, height);
            _status.Position = new Position(1, height);
            _status.ViewSize = new Size(width, 1);
            UpdateCodeVisibility();
        }

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
        FileShareState state = _service.State;
        if (_shown is null || !SameShown(_shown, state))
        {
            _shown = state;
            _url.Text = state.Url ?? string.Empty;
            if (state.QrModules is null)
            {
                _code.SetModules(null);
            }
            else if (!ReferenceEquals(_code.LastModules, state.QrModules))
            {
                _code.SetModules(state.QrModules);
            }

            UpdateCodeVisibility();
        }

        _status.Text = startFailure is not null ? Reason(startFailure) : StatusFor(state);
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
        _url.Text = string.Empty;
        _code.SetModules(null);
        _status.Text = UiText.Get(key, english);
    }

    /// <summary>The code is shown only when the dialog is tall enough and the code fits its width.</summary>
    private void UpdateCodeVisibility()
    {
        _code.IsVisible = _innerHeight >= MinimumHeightForCode
            && _code.ViewSize.Width > 0
            && _code.ViewSize.Width <= _innerWidth - 2;
    }

    private void LayOutWarning(int width, int height)
    {
        IReadOnlyList<string> lines = WrapWords(UiText.Get("FileTransfer.CertificateWarning", "The phone shows a certificate warning: accept it to download. Anyone with this address can read the selected files while this dialog is open."), width);
        for (int i = 0; i < WarningLines; i++)
        {
            Label label = _warning[i];
            label.Text = i < lines.Count ? lines[i] : string.Empty;
            label.Position = new Position(1, Math.Max(1, height - WarningLines + i));
            label.ViewSize = new Size(width, 1);
        }
    }

    /// <summary>Splits text into lines no wider than <paramref name="width"/>, at spaces where it can. A word longer than a line is cut.</summary>
    internal static IReadOnlyList<string> WrapWords(string text, int width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        List<string> lines = [];
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

        return lines;
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

/// <summary>Paints a QR matrix as text in black on white, two module rows to a line, so a dark code reads as dark on any theme.</summary>
internal sealed class QrCodeView : Control
{
    private IReadOnlyList<string> _lines = [];

    public QrCodeView(IThemeService themeService) : base(themeService)
    {
    }

    /// <summary>The matrix the lines were drawn from, so an idle pass does not encode the same code again.</summary>
    public bool[,]? LastModules { get; private set; }

    public void SetModules(bool[,]? modules)
    {
        LastModules = modules;
        _lines = modules is null ? [] : iConsole.Qr.QrTextRenderer.Render(modules);
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
