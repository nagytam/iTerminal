using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Checksums;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Services.Properties;
using iTerminal.Core.Services.Reputation;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Shift+F12: the MD5, SHA-1, SHA-256 and SHA-512 of the file under the cursor, on any drive.
///
/// <list type="bullet">
/// <item><b>The file is read once, on a background task</b> (<see cref="ComputeTask"/>), with the
/// percentage shown meanwhile; closing the dialog stops the read.</item>
/// <item><b>A checksum typed into the box is compared as it is typed</b>, against whichever
/// algorithm its length says it is — there is no clipboard to paste from yet, and a download page's
/// checksum is the thing one has in the other window.</item>
/// <item><b>A checksum written down beside the file is checked without being asked</b>
/// (<c>name.sha256</c>, <c>SHA256SUMS</c>): that file is there to be checked against.</item>
/// <item><b>F2 writes <c>name.sha256</c></b> in the format the common tools read — never over a
/// file that says something else.</item>
/// <item><b>F4 asks VirusTotal about the SHA-256, and only when pressed</b>: the hash is sent, never the
/// file. The key is a password-manager entry titled <c>VirusTotal…</c>. The look-up runs on its own
/// task, which closing the dialog (or a newer F4) cancels; F5 opens the VirusTotal page.</item>
/// </list>
/// </summary>
public class ChecksumDialog : Dialog
{
    private const int CaptionWidth = 10;

    private readonly IChecksumService _checksumService;
    private readonly IHashReputationService _reputationService;
    private readonly IPasswordManager _passwords;
    private readonly IFileLauncher _launcher;
    private readonly InfoLines _reputationLines;
    private CancellationTokenSource? _lookupCancellation;
    private int _lookupGeneration;
    private volatile ReputationResult? _reputation;
    private volatile string? _reputationNote;
    private bool _reputationFailed;
    private readonly InfoLines _info;
    private readonly TextBox _textBoxCompare;
    private readonly InfoLines _verdict;
    private CancellationTokenSource? _cancellation;
    private long _bytesRead;
    private string _lastCompare = string.Empty;
    private Dictionary<int, ChecksumAlgorithm> _hashRows = [];
    private string _saveStatus = string.Empty;
    private bool _saveFailed;

    public ChecksumDialog(
        IChecksumService checksumService,
        IHashReputationService reputationService,
        IPasswordManager passwords,
        IFileLauncher launcher,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(ChecksumDialog), themeService)
    {
        _checksumService = checksumService;
        _reputationService = reputationService;
        _passwords = passwords;
        _launcher = launcher;
        Title = iConsole.Localization.UiText.Get("Menu.Checksums", "Checksums");
        _textBoxCompare = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Field.Compare", "Compare"), Placeholder = UiText.Get("Field.PasteExpectedHash", "paste the expected hash") };
        _info = new InfoLines(themeService);
        _verdict = new InfoLines(themeService);
        _reputationLines = new InfoLines(themeService);

        // The box first: it is the one thing to type into, and the first focusable control is
        // where the cursor lands.
        AddControl(_textBoxCompare);
        AddControl(_info);
        AddControl(_verdict);
        AddControl(_reputationLines);
    }

    /// <summary>The last F4 look-up; nothing waits on it, it exists so a test can.</summary>
    public Task LookupTask { get; private set; } = Task.CompletedTask;

    /// <summary>What the dialog says about the reputation look-up.</summary>
    public string Reputation => _reputationLines.Text;

    /// <summary>The file being read.</summary>
    public Url? Url { get; private set; }

    /// <summary>The checksums, once the file has been read.</summary>
    public ChecksumResult? Result { get; private set; }

    /// <summary>Why the file could not be read, when it could not.</summary>
    public string? Error { get; private set; }

    /// <summary>A checksum found written down beside the file, if any.</summary>
    public ExpectedChecksum? Written { get; private set; }

    /// <summary>The read started when the dialog opened; nothing waits on it, it exists so a test can.</summary>
    public Task ComputeTask { get; private set; } = Task.CompletedTask;

    public string Compare
    {
        get => _textBoxCompare.Text ?? string.Empty;
        set => _textBoxCompare.Text = value ?? string.Empty;
    }

    /// <summary>What the dialog says about <see cref="Compare"/> and about saving.</summary>
    public string Verdict => _verdict.Text;

    /// <summary>The facts and the checksums as drawn.</summary>
    public string Details => _info.Text;

    private IFileSystem? _fileSystem;

    /// <summary>Sets the file; call before showing the dialog. <paramref name="fileSystem"/> is what reads it on a drive other than the local disk.</summary>
    public void Load(Url url, IFileSystem? fileSystem)
    {
        Url = url;
        _fileSystem = fileSystem;
        Result = null;
        Error = null;
        Written = null;
        CancelLookup();
        _reputation = null;
        _reputationNote = null;
        Compare = string.Empty;
        _lastCompare = string.Empty;
        _saveStatus = string.Empty;
        _saveFailed = false;
        Interlocked.Exchange(ref _bytesRead, 0);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        StartComputing();
        UpdateLines();
    }

    private void StartComputing()
    {
        _cancellation?.Cancel();
        var cancellation = _cancellation = new CancellationTokenSource();
        var url = Url;
        if (url is null)
        {
            return;
        }

        // Nothing slow before the dialog appears: the read, and the look for a written checksum
        // (a few small files), both happen behind it.
        ComputeTask = Task.Run(async () =>
        {
            try
            {
                if (url.ContainerType == ContainerType.FileSystem)
                {
                    Written = ChecksumService.FindWrittenChecksum(url.FullName);
                }

                var progress = new InlineProgress(bytes => Interlocked.Exchange(ref _bytesRead, bytes));
                var result = await _checksumService.ComputeAsync(url, _fileSystem!, progress, cancellation.Token);
                if (!cancellation.IsCancellationRequested)
                {
                    Result = result;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    Error = exception.Message;
                }
            }
        });
    }

    /// <summary><see cref="Progress{T}"/> posts to a synchronization context; this reports where it is called.</summary>
    private sealed class InlineProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    public override void OnInitializeLayout()
    {
        // A SHA-512 wraps over more rows the narrower the dialog is, so the facts block grows
        // with it: the verdict about a written checksum is the last row, and the first to be lost.
        var width = Parent is not null ? Math.Clamp((int)Parent.ViewSize.Width - 4, 50, 80) : (int)ViewSize.Width;
        var infoHeight = InfoHeight(Math.Max(16, width - 4 - CaptionWidth));
        if (Parent is not null)
        {
            CenterInParent(new Size(width, Math.Min(infoHeight + 10, Math.Max(17, (int)Parent.ViewSize.Height - 2))));
        }

        var inner = Math.Max(1, ViewSize.Width - 4);
        _info.Position = new Position(2, 1);
        _info.ViewSize = new Size(inner, infoHeight);
        _textBoxCompare.Position = new Position(2, infoHeight + 2);
        _textBoxCompare.ViewSize = new Size(inner, 1);
        _textBoxCompare.ScrollableModel.ViewSize = _textBoxCompare.ViewSize;
        _verdict.Position = new Position(2, infoHeight + 3);
        _verdict.ViewSize = new Size(inner, 2);
        _reputationLines.Position = new Position(2, infoHeight + 5);
        _reputationLines.ViewSize = new Size(inner, 3);
        base.OnInitializeLayout();
    }

    /// <summary>The read lands on a worker and the box is typed into: the idle pass brings the lines in step with both.</summary>
    public override void OnIdle()
    {
        base.OnIdle();
        UpdateLines();
    }

    /// <summary>Rows the facts need at most: file, size, a gap, every hash wrapped to <paramref name="valueWidth"/>, a gap and the written checksum's verdict.</summary>
    internal static int InfoHeight(int valueWidth)
    {
        int rows = 5;
        foreach (int length in new[] { 32, 40, 64, 128 })
        {
            rows += (length + valueWidth - 1) / valueWidth;
        }

        return Math.Max(10, rows);
    }

    private static string Row(string caption, string value) => caption.PadRight(CaptionWidth) + value;

    private void UpdateLines()
    {
        var url = Url;
        if (url is null)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var valueWidth = Math.Max(16, (int)_info.ViewSize.Width - CaptionWidth);
        var lines = new List<InfoLines.Line>
        {
            new(Row("File", iTerminal.Core.Commands.FocusedEntry.NameOf(url).Shrink(valueWidth) ?? string.Empty)),
        };

        var result = Result;
        if (Error is { } error)
        {
            lines.Add(new(Row("Failed", error.Shrink(valueWidth) ?? string.Empty), colorScheme.Error));
        }
        else if (result is null)
        {
            var read = Interlocked.Read(ref _bytesRead);
            var percent = url.Length > 0 ? $" {Math.Min(100, read * 100 / url.Length)}%" : string.Empty;
            lines.Add(new(Row("Reading…", $"{read.ToFileSize().Trim().Replace("  ", " ")}{percent}")));
        }
        else
        {
            lines.Add(new(Row("Size", FilePropertiesService.FormatSize(result.Bytes))));
        }

        lines.Add(default);
        Dictionary<int, ChecksumAlgorithm> hashRows = [];
        foreach (var algorithm in ChecksumService.Algorithms)
        {
            var caption = ChecksumService.DisplayName(algorithm);
            if (result is null)
            {
                lines.Add(new(Row(caption, Error is null ? "…" : string.Empty), colorScheme.DisabledForeground));
                continue;
            }

            // A SHA-512 is 128 characters: over as many rows as it takes, rather than cut.
            var hash = result.Hashes[algorithm];
            for (var start = 0; start < hash.Length; start += valueWidth)
            {
                hashRows[lines.Count] = algorithm;
                lines.Add(new(Row(start == 0 ? caption : string.Empty, hash.Substring(start, Math.Min(valueWidth, hash.Length - start)))));
            }
        }

        if (Written is { } written)
        {
            lines.Add(default);
            lines.Add(result is null
                ? new(Row("Written", $"{written.Source}: checking…"))
                : string.Equals(result.Hashes[written.Algorithm], written.Hash, StringComparison.OrdinalIgnoreCase)
                    ? new(Row("Written", $"✓ matches {written.Source}"), Color.Green)
                    : new(Row("Written", $"✗ does NOT match {written.Source}"), colorScheme.Error));
        }

        _hashRows = hashRows;
        _info.Lines = lines;

        var compare = Compare;
        if (compare != _lastCompare)
        {
            // Typing again clears what F2 last said: the line is about the box now.
            _lastCompare = compare;
            _saveStatus = string.Empty;
        }

        var verdict = new List<InfoLines.Line>();
        if (CompareVerdict(compare, result) is { } answer)
        {
            verdict.Add(new(answer.Text, answer.Matches switch { true => Color.Green, false => colorScheme.Error, _ => null }));
        }

        if (_saveStatus.Length > 0)
        {
            verdict.Add(new(_saveStatus, _saveFailed ? colorScheme.Error : null));
        }

        _verdict.Lines = verdict;
        _reputationLines.Lines = ReputationRows(colorScheme, Math.Max(16, (int)_reputationLines.ViewSize.Width));
    }

    private List<InfoLines.Line> ReputationRows(ColorScheme colorScheme, int width)
    {
        var rows = new List<InfoLines.Line>();
        if (_reputation is { } reputation)
        {
            rows.Add(ReputationHeadline(reputation, colorScheme, width));
            if (reputation.Status == ReputationStatus.Found)
            {
                var first = reputation.FirstSeen is { } f ? f.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "–";
                var last = reputation.LastSeen is { } l ? l.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : "–";
                rows.Add(new(string.Format(UiText.Get("Reputation.Seen", "First seen {0}, last seen {1}"), first, last).Shrink(width) ?? string.Empty));
            }

            if (reputation.Link is not null)
            {
                rows.Add(new(UiText.Get("Reputation.OpenHint", "F5 opens the VirusTotal page in the browser").Shrink(width) ?? string.Empty, colorScheme.DisabledForeground));
            }
        }
        else if (_reputationNote is { } note)
        {
            rows.Add(new(note.Shrink(width) ?? string.Empty, _reputationFailed ? colorScheme.Error : null));
        }

        return rows;
    }

    private static InfoLines.Line ReputationHeadline(ReputationResult reputation, ColorScheme colorScheme, int width)
    {
        switch (reputation.Status)
        {
            case ReputationStatus.Found:
                var text = string.Format(UiText.Get("Reputation.Detections", "VirusTotal: {0}/{1} engines flag this file"), reputation.Detections, reputation.Total);
                return new(text.Shrink(width) ?? string.Empty, reputation.Detections > 0 ? colorScheme.Error : Color.Green);
            case ReputationStatus.NotFound:
                return new(UiText.Get("Reputation.NotFound", "VirusTotal has never seen this file (open the page to upload it yourself).").Shrink(width) ?? string.Empty);
            case ReputationStatus.RateLimited:
                var message = reputation.RetryAfter is { } wait
                    ? string.Format(UiText.Get("Reputation.RateLimitedAfter", "VirusTotal rate limit reached: try again in {0} s."), (int)Math.Ceiling(wait.TotalSeconds))
                    : UiText.Get("Reputation.RateLimited", "VirusTotal rate limit reached: try again in a minute.");
                return new(message.Shrink(width) ?? string.Empty, colorScheme.Error);
            case ReputationStatus.Offline:
                return new(UiText.Get("Reputation.Offline", "Could not reach VirusTotal: you seem to be offline.").Shrink(width) ?? string.Empty, colorScheme.Error);
            case ReputationStatus.InvalidKey:
                return new(UiText.Get("Reputation.InvalidKey", "VirusTotal refused the API key: check the Password Manager entry.").Shrink(width) ?? string.Empty, colorScheme.Error);
            default:
                return new(string.Format(UiText.Get("Reputation.Error", "VirusTotal look-up failed: {0}"), reputation.Message ?? "?").Shrink(width) ?? string.Empty, colorScheme.Error);
        }
    }

    private void SetReputationNote(string note, bool failed)
    {
        _reputation = null;
        _reputationNote = note;
        _reputationFailed = failed;
    }

    private void CancelLookup()
    {
        // A pending look-up is of no use once the dialog is gone or shows another file; its answer is
        // dropped by the generation as well as by the token.
        Interlocked.Increment(ref _lookupGeneration);
        _lookupCancellation?.Cancel();
        _lookupCancellation = null;
    }

    /// <summary>
    /// F4: asks VirusTotal about the file's SHA-256. Never started by itself. Only the hash leaves the
    /// machine, and the key is read from the password manager for this one call.
    /// </summary>
    public void LookUpReputation()
    {
        if (Result is not { } result)
        {
            SetReputationNote(UiText.Get("Reputation.Wait", "Wait until the file has been read, then press F4."), true);
        }
        else if (VirusTotalKey.Resolve(_passwords) is not { } key)
        {
            SetReputationNote(UiText.Get("Reputation.NoKey", "No VirusTotal key: add a Password Manager entry titled \"VirusTotal\" with your API key as its password."), true);
        }
        else
        {
            CancelLookup();
            var cancellation = _lookupCancellation = new CancellationTokenSource();
            var generation = Volatile.Read(ref _lookupGeneration);
            var hash = result.Hashes[ChecksumAlgorithm.Sha256];
            SetReputationNote(UiText.Get("Reputation.Asking", "Asking VirusTotal about the SHA-256 (only the hash is sent)…"), false);
            LookupTask = Task.Run(async () =>
            {
                ReputationResult answer;
                try
                {
                    answer = await _reputationService.LookUpAsync(hash, key, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    answer = new ReputationResult(ReputationStatus.Error, Message: exception.Message);
                }

                if (!cancellation.IsCancellationRequested && generation == Volatile.Read(ref _lookupGeneration))
                {
                    _reputation = answer;
                }
            });
        }

        UpdateLines();
        StateHasChanged();
    }

    /// <summary>F5: opens the VirusTotal page of a file the look-up has answered for.</summary>
    public void OpenReputationPage()
    {
        if (_reputation?.Link is not { } link)
        {
            return;
        }

        var generation = Volatile.Read(ref _lookupGeneration);
        _ = Task.Run(async () =>
        {
            var outcome = await _launcher.OpenUrlAsync(link);
            if (!outcome.Started && generation == Volatile.Read(ref _lookupGeneration))
            {
                SetReputationNote(outcome.Failure ?? UiText.Get("Reputation.OpenFailed", "The browser could not be started."), true);
            }
        });
    }

    /// <summary>
    /// What to say about a checksum typed into the box: nothing while the box is empty, that it is
    /// not a checksum, that it is being waited for, or whether it matches — and which algorithm.
    /// </summary>
    internal static (string Text, bool? Matches)? CompareVerdict(string? typed, ChecksumResult? result)
    {
        var normalized = ChecksumService.Normalize(typed);
        if (normalized.Length == 0)
        {
            return null;
        }

        if (ChecksumService.AlgorithmFor(normalized) is not { } algorithm)
        {
            return ($"Not a checksum yet: {normalized.Length} characters (32, 40, 64 or 128 expected).", null);
        }

        var name = ChecksumService.DisplayName(algorithm);
        if (result is null)
        {
            return ($"A {name} — compared as soon as the file is read.", null);
        }

        return string.Equals(result.Hashes[algorithm], normalized, StringComparison.OrdinalIgnoreCase)
            ? ($"✓ Matches the {name}.", true)
            : ($"✗ Does NOT match the {name}.", false);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("F2", UiText.Get("DialogHint.SaveChecksumsNextToFiles", "Save the checksums next to the files"));
        yield return ("F3", UiText.Get("DialogHint.CopySha256", "Copy the SHA-256 checksum"));
        yield return ("F4", UiText.Get("DialogHint.LookUpReputation", "Look the SHA-256 up on VirusTotal (sends only the hash)"));
        yield return ("F5", UiText.Get("DialogHint.OpenReputationPage", "Open the VirusTotal page in the browser"));
        yield return ("Click", UiText.Get("DialogHint.CopyChecksumUnderPointer", "Copy the checksum under the pointer"));
        yield return ("Enter", UiText.Get("DialogHint.CloseReportCompareEmpty", "Close the report while the compare box is empty"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.F2 && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            SaveChecksumFile();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F3 && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            CopyHash(ChecksumAlgorithm.Sha256);
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F4 && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            LookUpReputation();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F5 && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            OpenReputationPage();
            return true;
        }

        // With nothing typed there is nothing for Enter to do in the box, so it dismisses the report.
        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift && Compare.Length == 0)
        {
            Close();
            return true;
        }

        var handled = await base.HandleKeyEventAsync(keyEvent);
        UpdateLines();
        return handled;
    }

    /// <summary>A click on a checksum copies it to the clipboard, to be pasted where it is compared.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.LeftClicked && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            long row = mouseEvent.Position.Y - _info.AbsolutePosition.Y;
            long column = mouseEvent.Position.X - _info.AbsolutePosition.X;
            if (column >= 0 && column < _info.ViewSize.Width && _hashRows.TryGetValue((int)row, out ChecksumAlgorithm algorithm))
            {
                CopyHash(algorithm);
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>Puts one of the checksums on the clipboard and says so.</summary>
    public bool CopyHash(ChecksumAlgorithm algorithm)
    {
        if (Result is not { } result)
        {
            _saveStatus = "Nothing to copy yet: the file is still being read.";
            _saveFailed = true;
            UpdateLines();
            StateHasChanged();
            return false;
        }

        string name = ChecksumService.DisplayName(algorithm);
        Task copy = TextClipboard.Current.SetTextAsync(result.Hashes[algorithm]);
        _saveStatus = $"Copied the {name} to the clipboard.";
        _saveFailed = false;
        _ = copy.ContinueWith(task =>
        {
            if (task.IsFaulted)
            {
                _saveStatus = $"Could not copy the {name}: {task.Exception?.GetBaseException().Message}";
                _saveFailed = true;
                StateHasChanged();
            }
        }, TaskScheduler.Default);
        UpdateLines();
        StateHasChanged();
        return true;
    }

    /// <summary>
    /// Writes <c>name.sha256</c> beside the file. Not over one that says something else: that file
    /// may be the publisher's, and the one thing it must not be is silently replaced by whatever
    /// the file hashes to today.
    /// </summary>
    public bool SaveChecksumFile()
    {
        var url = Url;
        (_saveStatus, _saveFailed) = Save();
        UpdateLines();
        StateHasChanged();
        return !_saveFailed;

        (string Status, bool Failed) Save()
        {
            if (url is null || Result is not { } result)
            {
                return ("Nothing to save yet: the file is still being read.", true);
            }

            if (url.ContainerType != ContainerType.FileSystem)
            {
                return ("A checksum file can only be written beside a file on the local disk.", true);
            }

            var name = Path.GetFileName(url.FullName);
            var target = url.FullName + ChecksumService.Extension(ChecksumAlgorithm.Sha256);
            var line = ChecksumService.FormatChecksumLine(result.Hashes[ChecksumAlgorithm.Sha256], name);
            try
            {
                if (File.Exists(target))
                {
                    var existing = ChecksumService.ParseChecksumFile(File.ReadAllText(target), name);
                    return string.Equals(existing, result.Hashes[ChecksumAlgorithm.Sha256], StringComparison.OrdinalIgnoreCase)
                        ? ($"{Path.GetFileName(target)} already says so.", false)
                        : ($"{Path.GetFileName(target)} exists and says something else — not overwritten.", true);
                }

                File.WriteAllText(target, line + "\n");
                return ($"Saved {Path.GetFileName(target)}.", false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return ($"Could not save {Path.GetFileName(target)}: {exception.Message}", true);
            }
        }
    }

    public override void Close()
    {
        // The read is of no use to anyone once the dialog is gone, and over ssh it is a transfer.
        _cancellation?.Cancel();
        CancelLookup();
        base.Close();
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var footer = " F2:save .sha256  F3 or click:copy  F4:VirusTotal  F5:open  Esc:close ".FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}
