using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Services.Signing;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Ctrl+Shift+F7: the signatures of the file under the cursor, and signing it.
///
/// <list type="bullet">
/// <item><b>Opening checks.</b> The embedded Authenticode signature (Windows) and any detached
/// <c>.asc</c>/<c>.sig</c>/<c>.gpg</c>/<c>.sign</c> beside the file are verified on a background
/// task (<see cref="WorkTask"/>), each with its own verdict line: a file can be validly signed one
/// way and not at all another, and one line would have to pick.</item>
/// <item><b>One box, three meanings.</b> What is typed in <c>Key</c> is the gpg key id (F2), the
/// certificate thumbprint (F3), or the PEM private key file (F4) — and, for F5, the PEM public key a
/// <c>.sig</c> is checked against. Blank means gpg's default key, or the only code-signing
/// certificate there is.</item>
/// <item><b>Signing never overwrites</b> a signature file; an Authenticode signature is embedded in
/// the file, which is why that key is the one that cannot be taken lightly and says so.</item>
/// <item><b>Local files only</b>: gpg and PowerShell are handed a path.</item>
/// </list>
/// </summary>
public class SignatureDialog : Dialog
{
    private readonly ISignatureService _signatureService;
    private readonly INotificationService _notifications;
    private readonly IContentModelService _contentModelService;
    private readonly InfoLines _info;
    private readonly TextBox _textBoxKey;
    private CancellationTokenSource? _cancellation;
    private volatile string _busy = string.Empty;
    private string _status = string.Empty;
    private bool _statusFailed;

    public SignatureDialog(
        ISignatureService signatureService,
        INotificationService notifications,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(SignatureDialog), themeService)
    {
        _signatureService = signatureService;
        _notifications = notifications;
        _contentModelService = contentModelService;
        Title = "Signatures";
        _textBoxKey = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Key" };
        _info = new InfoLines(themeService);
        AddControl(_textBoxKey);
        AddControl(_info);
    }

    /// <summary>The file.</summary>
    public string? FilePath { get; private set; }

    public IReadOnlyList<SignatureVerdict> Verdicts { get; private set; } = [];

    public SigningCapabilities? Capabilities { get; private set; }

    public IReadOnlyList<SigningCertificate> Certificates { get; private set; } = [];

    /// <summary>The check or signing under way; nothing waits on it, it exists so a test can.</summary>
    public Task WorkTask { get; private set; } = Task.CompletedTask;

    /// <summary>What the Key box holds.</summary>
    public string Key
    {
        get => _textBoxKey.Text ?? string.Empty;
        set => _textBoxKey.Text = value ?? string.Empty;
    }

    /// <summary>The last signing outcome, as drawn.</summary>
    public string Status => _status;

    /// <summary>The lines as drawn.</summary>
    public string Details => _info.Text;

    public void Load(string filePath)
    {
        FilePath = filePath;
        Verdicts = [];
        _status = string.Empty;
        _statusFailed = false;
        Key = string.Empty;
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        StartVerifying();
        UpdateLines();
    }

    /// <summary>Checks the file's signatures again, with <see cref="Key"/> as the public key a <c>.sig</c> needs.</summary>
    public void StartVerifying()
    {
        var path = FilePath;
        if (path is null)
        {
            return;
        }

        var publicKey = Key.Trim();
        Run("Checking…", async token =>
        {
            Capabilities ??= await _signatureService.GetCapabilitiesAsync(token);
            if (Capabilities.Authenticode && Certificates.Count == 0)
            {
                Certificates = await _signatureService.GetCertificatesAsync(token);
            }

            Verdicts = await _signatureService.VerifyAsync(path, null, publicKey.Length > 0 ? publicKey : null, token);
        });
    }

    /// <summary>Signs with <paramref name="method"/>, using <see cref="Key"/> as that method's key; checks again afterwards.</summary>
    public void StartSigning(SignatureMethod method)
    {
        var path = FilePath;
        if (path is null)
        {
            return;
        }

        var key = Key.Trim();
        Run("Signing…", async token =>
        {
            var identifier = method == SignatureMethod.Pem ? null : key;
            if (method == SignatureMethod.Authenticode && key.Length == 0)
            {
                Certificates = await _signatureService.GetCertificatesAsync(token);
                if (Certificates.Count == 1)
                {
                    identifier = Certificates[0].Thumbprint;
                }
                else
                {
                    (_status, _statusFailed) = (Certificates.Count == 0
                        ? "No code-signing certificate in your store."
                        : "Several certificates: type the thumbprint of the one to use in Key.", true);
                    return;
                }
            }

            var result = await _signatureService.SignAsync(path, method, identifier, method == SignatureMethod.Pem ? key : null, token);
            (_status, _statusFailed) = (result.Message, !result.Success);
            _notifications.Notify(
                result.Success ? "File signed" : "Signing failed",
                $"{Path.GetFileName(path)}: {result.Message}",
                result.Success ? NotificationSeverity.Success : NotificationSeverity.Error);
            if (result.Success)
            {
                Verdicts = await _signatureService.VerifyAsync(path, null, null, token);
                RefreshPanels();
            }
        });
    }

    private void Run(string busy, Func<CancellationToken, Task> work)
    {
        _cancellation?.Cancel();
        var cancellation = _cancellation = new CancellationTokenSource();
        _busy = busy;
        WorkTask = Task.Run(async () =>
        {
            try
            {
                await work(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                (_status, _statusFailed) = (exception.Message, true);
            }
            finally
            {
                if (!cancellation.IsCancellationRequested)
                {
                    _busy = string.Empty;
                }
            }
        });
    }

    private void RefreshPanels()
    {
        foreach (var model in _contentModelService?.ContentModels.ToList() ?? [])
        {
            try
            {
                model.Refresh(forceRefresh: true);
            }
            catch (Exception)
            {
                // A panel that cannot be re-read is not the signing's failure.
            }
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 4, 50, 80), 18));
        }

        var inner = Math.Max(1, ViewSize.Width - 4);
        _info.Position = new Position(2, 1);
        _info.ViewSize = new Size(inner, Math.Max(1, ViewSize.Height - 5));
        _textBoxKey.Position = new Position(2, ViewSize.Height - 3);
        _textBoxKey.ViewSize = new Size(inner, 1);
        _textBoxKey.ScrollableModel.ViewSize = _textBoxKey.ViewSize;
        base.OnInitializeLayout();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        UpdateLines();
    }

    private void UpdateLines()
    {
        var path = FilePath;
        if (path is null)
        {
            return;
        }

        var colors = ThemeService.CurrentColorScheme;
        var width = Math.Max(16, (int)_info.ViewSize.Width);
        var lines = new List<InfoLines.Line> { new(("File  " + Path.GetFileName(path)).Shrink(width) ?? string.Empty), default };

        foreach (var verdict in Verdicts)
        {
            var (mark, color) = verdict.Status switch
            {
                SignatureStatus.Valid => ("✓", (Color?)Color.Green),
                SignatureStatus.Invalid => ("✗", colors.Error),
                SignatureStatus.NotSigned => ("–", colors.DisabledForeground),
                SignatureStatus.UnknownSigner => ("?", Color.Yellow),
                _ => ("!", colors.Error),
            };
            lines.Add(new($"{mark} {MethodName(verdict.Method)}: {verdict.Message}".Shrink(width) ?? string.Empty, color));
            if (verdict.Signer.Length > 0)
            {
                lines.Add(new(("    by " + verdict.Signer).Shrink(width) ?? string.Empty, colors.DisabledForeground));
            }
        }

        if (_busy.Length > 0)
        {
            lines.Add(new(_busy, colors.DisabledForeground));
        }
        else if (Verdicts.Count == 0 && _status.Length == 0)
        {
            lines.Add(new("…", colors.DisabledForeground));
        }

        if (_status.Length > 0)
        {
            lines.Add(default);
            lines.Add(new(_status.Shrink(width) ?? string.Empty, _statusFailed ? colors.Error : Color.Green));
        }

        if (Capabilities is { } capabilities && !capabilities.Gpg)
        {
            lines.Add(new("gpg is not installed: .asc signatures can be neither made nor checked.", colors.DisabledForeground));
        }

        if (Capabilities?.Authenticode == true && Certificates.Count > 0)
        {
            lines.Add(new($"Certificates: {string.Join("; ", Certificates.Select(c => $"{c.Thumbprint[..8]}… {c.Subject}"))}".Shrink(width) ?? string.Empty, colors.DisabledForeground));
        }

        _info.Lines = lines;
    }

    private static string MethodName(SignatureMethod method) => method switch
    {
        SignatureMethod.Gpg => "OpenPGP",
        SignatureMethod.Authenticode => "Authenticode",
        _ => "Key signature",
    };

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            var handled = true;
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.F2:
                    StartSigning(SignatureMethod.Gpg);
                    break;
                case ConsoleKey.F3:
                    StartSigning(SignatureMethod.Authenticode);
                    break;
                case ConsoleKey.F4:
                    StartSigning(SignatureMethod.Pem);
                    break;
                case ConsoleKey.F5:
                    StartVerifying();
                    break;
                case ConsoleKey.Enter when _busy.Length == 0:
                    // A report with nothing to type: Enter dismisses it, as it does a message box.
                    Close();
                    return true;
                default:
                    handled = false;
                    break;
            }

            if (handled)
            {
                UpdateLines();
                return true;
            }
        }

        var result = await base.HandleKeyEventAsync(keyEvent);
        UpdateLines();
        return result;
    }

    public override void Close()
    {
        _cancellation?.Cancel();
        base.Close();
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var footer = " F2:sign gpg  F3:sign Authenticode  F4:sign key file  F5:check again  Esc:close ".FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}
