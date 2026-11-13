using System.Globalization;
using iConsole.Certificates;
using iConsole.Controls;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls.Viewers;
using iConsole.Layouts;
using iConsole.Data;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Dialogs;

/// <summary>
/// The certificate viewer: the certificates of a PEM or DER file in a picker, the selected one's fields below it.
/// F3 opens it only for a file that holds a certificate; any other file falls back to the text viewer through
/// <see cref="OnFallback"/>, which the dialog runs after it has closed itself.
///
/// <para>The file is read on a worker thread. The result is applied once, on the thread that owns the controls, from
/// <see cref="OnIdle"/>. Setting <see cref="Url"/> starts a new request; a result for an earlier URL is never applied.
/// The parsed result is kept for one URL, so the shared instance never shows one file's certificates for another.</para>
/// </summary>
public class CertificateViewerDialog : Dialog
{
    private const string CloseActionView = " [X]";

    private readonly ComboBox<CertificateInfo> _picker;
    private readonly TextViewerControl _textViewer;
    private readonly Label _statusLabel;
    private readonly INotificationService _notifications;
    private Url? _url;
    private Task<CertificateLoad>? _loadTask;
    private CancellationTokenSource? _cancellation;
    private CertificateLoad? _cached;
    private bool _applied;
    private Task? _fallbackTask;

    public CertificateViewerDialog(IThemeService themeService, INotificationService notifications)
        : base(nameof(CertificateViewerDialog), themeService)
    {
        _notifications = notifications;
        _picker = new ComboBox<CertificateInfo>(themeService)
        {
            Label = UiText.Get("Certificate.Picker.Label", "Certificate"),
            DisplaySelector = PickerLabel,
        };
        _picker.SelectionChanged += (_, _) => ShowSelectedCertificate();
        _textViewer = new TextViewerControl(false, themeService);
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_picker);
        AddControl(_textViewer);
        AddControl(_statusLabel);
    }

    /// <summary>
    /// The file shown. Setting it starts a new request: the run-once flag is cleared here, so the next apply step
    /// handles the new file. <c>OnInitializeAsync</c> never clears it.
    /// </summary>
    public Url? Url
    {
        get => _url;
        set
        {
            _url = value;
            _applied = false;
        }
    }

    /// <summary>The clock the validity line is judged against. A test sets it; the viewer uses the real time.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>What to show when the file holds no certificate. Runs after the dialog has closed itself.</summary>
    public Func<Task>? OnFallback { get; set; }

    /// <summary>The read of the current request, for a test or caller to wait on.</summary>
    public Task LoadTask => (Task?)_loadTask ?? Task.CompletedTask;

    /// <summary>The fallback that is running or has finished, for a test to wait on. It never faults.</summary>
    public Task FallbackTask => _fallbackTask ?? Task.CompletedTask;

    /// <summary>The certificates of the current file, in the picker's order.</summary>
    public IReadOnlyList<CertificateInfo> Certificates => _picker.Items;

    /// <summary>The certificate on screen, or null while the file loads or after a fallback.</summary>
    public CertificateInfo? CurrentCertificate => _picker.SelectedItem;

    /// <summary>The fields of the selected certificate.</summary>
    public IList<string> Lines => _textViewer.TextModel.Lines;

    /// <summary>The status line: the loading note, the position, the validity, or the number of private-key blocks not shown.</summary>
    public string Status => _statusLabel.Text;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        // The dialog is shared: a new show starts from nothing, and the last request's read is cancelled.
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _picker.SetItems([]);
        _textViewer.TextModel.Lines.ReplaceAll([]);
        _statusLabel.Text = string.Empty;
        if (_url is null)
        {
            _statusLabel.Text = UiText.Get("Viewer.NothingToShow", "Nothing to show");
            _loadTask = null;
            return;
        }

        string fullUrl = _url.FullUrl;
        string path = _url.Path;
        // The cache is valid only while the file is the one that was read: same length and last-write time.
        string stamp = FileStamp(path);
        if (_cached is { } cached && cached.FullUrl == fullUrl && stamp.Length > 0 && cached.Stamp == stamp)
        {
            _loadTask = Task.FromResult(cached);
            return;
        }

        _cached = null;
        CancellationToken token = _cancellation.Token;
        _statusLabel.Text = UiText.Get("Certificate.Status.Loading", "Reading the certificate…");
        _loadTask = Task.Run(() => Load(fullUrl, path, stamp, token), token);
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        _picker.SetFocus();
    }

    /// <summary>Applies a finished read once, on the UI thread: shows the certificates, or closes and falls back.</summary>
    public override void OnIdle()
    {
        base.OnIdle();
        if (_applied || _loadTask is not { IsCompleted: true } task)
        {
            return;
        }

        _applied = true;
        if (task.IsCanceled)
        {
            return;
        }

        if (task.IsFaulted)
        {
            // Load catches what it expects; anything else is an unexpected fault, reported rather than hidden.
            BeginFallback(_url?.FullUrl, UiText.Get("Certificate.Fallback.Failed", "Could not open the file"), task.Exception?.GetBaseException().Message);
            return;
        }

        CertificateLoad load = task.Result;
        if (load.Outcome == CertificateLoadOutcome.Cancelled || _url?.FullUrl != load.FullUrl)
        {
            return;
        }

        if (load.Outcome == CertificateLoadOutcome.Fallback)
        {
            BeginFallback(load.FullUrl, load.Reason, null);
            return;
        }

        _cached = load;
        _picker.SetItems(load.Certificates!);
        _picker.SelectedIndex = 0;
        // Selecting the first row raises no change when it is already selected, so the fields are shown here directly.
        ShowSelectedCertificate();
    }

    /// <summary>
    /// Closes the viewer and runs the text fallback. A read that failed says why as a notification; a file with no
    /// certificate and no fallback wired says so too, so F3 never does nothing silently.
    /// </summary>
    private void BeginFallback(string? fullUrl, string? reason, string? detail)
    {
        Func<Task>? fallback = OnFallback;
        Close();
        if (reason is not null && fallback is not null)
        {
            _notifications.Notify(reason, detail, NotificationSeverity.Warning);
        }

        _fallbackTask = RunFallbackAsync(fallback, reason, detail);
    }

    /// <summary>Runs the text fallback. It never throws: a failure is reported as a notification, so it cannot vanish inside <see cref="OnIdle"/>.</summary>
    private async Task RunFallbackAsync(Func<Task>? fallback, string? reason, string? detail)
    {
        if (fallback is null)
        {
            _notifications.Notify(
                UiText.Get("Certificate.Fallback.NoCertificate", "No certificate in this file"),
                reason ?? detail,
                NotificationSeverity.Info);
            return;
        }

        try
        {
            await fallback();
        }
        catch (Exception exception)
        {
            _notifications.Notify(
                UiText.Get("Certificate.Fallback.Failed", "Could not open the file"),
                exception.Message,
                NotificationSeverity.Error);
        }
    }

    private void ShowSelectedCertificate()
    {
        CertificateInfo? certificate = _picker.SelectedItem;
        if (certificate is null)
        {
            _textViewer.TextModel.Lines.ReplaceAll([]);
            return;
        }

        _textViewer.TextModel.Lines.ReplaceAll(CertificateFormatter.Lines(certificate));
        CertificateLoad? load = _cached;
        int position = _picker.SelectedIndex + 1;
        int count = load?.Certificates?.Count ?? 0;
        string status = string.Format(CultureInfo.CurrentCulture, UiText.Get("Certificate.Status.Count", "Certificate {0} of {1}"), position, count)
            + " · " + CertificateFormatter.Validity(certificate, Clock());
        if (load is { PrivateKeyBlocks: > 0 })
        {
            status += string.Format(CultureInfo.CurrentCulture, UiText.Get("Certificate.Status.PrivateKeys", " · {0} private key block(s) not shown"), load.PrivateKeyBlocks);
        }

        _statusLabel.Text = status;
        StateHasChanged();
    }

    /// <summary>Cancels a read that is still running when the dialog goes, so a closed viewer never keeps a worker.</summary>
    protected override void OnBeforeClose()
    {
        _cancellation?.Cancel();
        base.OnBeforeClose();
    }

    private static CertificateLoad Load(string fullUrl, string path, string stamp, CancellationToken token)
    {
        CertificateFileRead read = CertificateFileReader.ReadCapped(path);
        if (token.IsCancellationRequested)
        {
            return CertificateLoad.Cancelled(fullUrl);
        }

        if (read.Status == CertificateFileStatus.Overflow)
        {
            return CertificateLoad.Fallback(fullUrl, UiText.Get("Certificate.Fallback.TooLarge", "The file is larger than 1 MiB"));
        }

        if (read.Status != CertificateFileStatus.Ok)
        {
            return CertificateLoad.Fallback(fullUrl, UiText.Get("Certificate.Fallback.Unreadable", "The file could not be read"), read.Error);
        }

        CertificateReadResult parsed = CertificateReader.Read(read.Bytes);
        return parsed.Certificates.Count == 0
            ? CertificateLoad.Fallback(fullUrl)
            : new CertificateLoad(fullUrl, stamp, CertificateLoadOutcome.Certificates, parsed.Certificates, parsed.PrivateKeyBlocks);
    }

    /// <summary>The length and last-write time of a file, or empty when it cannot be read: an empty stamp is never cached.</summary>
    private static string FileStamp(string path)
    {
        try
        {
            FileInfo info = new(path);
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    /// <summary>The picker row: the subject, or a name or serial when the subject is empty, with the serial's tail to tell same-named certificates apart.</summary>
    private static string PickerLabel(CertificateInfo certificate)
    {
        string name = certificate.Subject.Length > 0
            ? certificate.Subject
            : certificate.SubjectAlternativeNames.FirstOrDefault() ?? certificate.SerialNumber;
        string serial = certificate.SerialNumber;
        return $"{name} [{serial[^Math.Min(8, serial.Length)..]}]";
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton(UiText.Get("Common.Close", "Close"), CloseActionView, new CloseSelfCommand(this), ThemeService));
    }

    /// <summary>The keys this dialog adds, for the help screen.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Up / Down", UiText.Get("Certificate.Hint.Step", "Show the previous or next certificate"));
    }

    private sealed class CloseSelfCommand(CertificateViewerDialog owner) : Command
    {
        public override Task ExecuteAsync(object? arg)
        {
            owner.Close();
            return Task.CompletedTask;
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
            Title = UiText.Get("Certificate.Title", "Certificate") + (Url is null ? string.Empty : " · " + Url.Shrink(Math.Max(4, (int)ViewSize.Width - 20)));
        }

        // Inside the border: the picker, the fields, and the status line on the last row.
        int width = Math.Max(0, (int)ViewSize.Width - 2);
        int content = Math.Max(1, (int)ViewSize.Height - 4);
        int statusRow = Math.Max(0, (int)ViewSize.Height - 2);

        _picker.Position = new Position(1, 1);
        _picker.ViewSize = new Size(width, 1);

        _textViewer.Position = new Position(1, 2);
        _textViewer.ViewSize = new Size(width, content);

        _statusLabel.Position = new Position(1, statusRow);
        _statusLabel.ViewSize = new Size(width, 1);

        base.OnInitializeLayout();

        Layout.Center(this);
    }

    /// <summary>The outcome of one read: certificates, a fallback to the text viewer, or a read that was cancelled.</summary>
    private enum CertificateLoadOutcome
    {
        Certificates,
        Fallback,
        Cancelled,
    }

    /// <summary>The result of one read, tagged with the URL it was made for.</summary>
    private sealed record CertificateLoad(
        string FullUrl,
        string Stamp,
        CertificateLoadOutcome Outcome,
        IReadOnlyList<CertificateInfo>? Certificates,
        int PrivateKeyBlocks,
        string? Reason = null,
        string? Detail = null)
    {
        /// <summary>A file with no certificate falls back silently; a read that failed passes its reason, which is shown.</summary>
        public static CertificateLoad Fallback(string fullUrl, string? reason = null, string? detail = null) =>
            new(fullUrl, string.Empty, CertificateLoadOutcome.Fallback, null, 0, reason, detail);

        public static CertificateLoad Cancelled(string fullUrl) => new(fullUrl, string.Empty, CertificateLoadOutcome.Cancelled, null, 0);
    }
}
