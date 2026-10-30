using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Services.Checksums;
using iTerminal.Core.Services.Properties;

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
/// </list>
/// </summary>
public class ChecksumDialog : Dialog
{
    private const int CaptionWidth = 10;

    private readonly IChecksumService _checksumService;
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
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(ChecksumDialog), themeService)
    {
        _checksumService = checksumService;
        Title = iConsole.Localization.UiText.Get("Menu.Checksums", "Checksums");
        _textBoxCompare = new TextBox(beepService, dateTimeProviderService, themeService) { Label = "Compare", Placeholder = "paste the expected hash" };
        _info = new InfoLines(themeService);
        _verdict = new InfoLines(themeService);

        // The box first: it is the one thing to type into, and the first focusable control is
        // where the cursor lands.
        AddControl(_textBoxCompare);
        AddControl(_info);
        AddControl(_verdict);
    }

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
            CenterInParent(new Size(width, Math.Min(infoHeight + 7, Math.Max(17, (int)Parent.ViewSize.Height - 2))));
        }

        var inner = Math.Max(1, ViewSize.Width - 4);
        _info.Position = new Position(2, 1);
        _info.ViewSize = new Size(inner, infoHeight);
        _textBoxCompare.Position = new Position(2, infoHeight + 2);
        _textBoxCompare.ViewSize = new Size(inner, 1);
        _textBoxCompare.ScrollableModel.ViewSize = _textBoxCompare.ViewSize;
        _verdict.Position = new Position(2, infoHeight + 3);
        _verdict.ViewSize = new Size(inner, 2);
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
        yield return ("F2", "Save the checksums next to the files");
        yield return ("F3", "Copy the SHA-256 checksum");
        yield return ("Click", "Copy the checksum under the pointer");
        yield return ("Enter", "Close the report while the compare box is empty");
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
        base.Close();
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var footer = " F2:save .sha256  F3 or click:copy  Esc:close ".FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}
