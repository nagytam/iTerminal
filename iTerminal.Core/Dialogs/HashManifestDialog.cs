using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.Checksums;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Shift+Alt+F3: a hash manifest (<c>SHA256SUMS</c>) for the marked rows or the whole folder tree, and
/// a check of a folder against one — on any drive a file can be read from.
///
/// <list type="bullet">
/// <item><b>F2 creates</b> the manifest beside the files (relative paths, <c>hash *name</c>, the chosen
/// algorithm, SHA-256 by default) — never over a manifest that says something else.</item>
/// <item><b>F3 verifies</b>: every row of the manifest is OK, changed, missing or unreadable, every
/// file the manifest does not list is extra, and a summary says how many of each.</item>
/// <item><b>Both run on a background task</b> (<see cref="RunTask"/>), each file read once, with a
/// percentage meanwhile. Esc stops a running one; closing the dialog stops it too.</item>
/// </list>
/// </summary>
public class HashManifestDialog : Dialog
{
    private readonly FormPanel _form;
    private readonly ComboBox<ChecksumAlgorithm> _comboAlgorithm;
    private readonly ResultList _list;
    private readonly Lock _lock = new();
    private CancellationTokenSource? _cancellation;
    private volatile string _status = string.Empty;
    private volatile bool _statusIsError;
    private volatile ManifestProgress? _progress;
    private volatile string _verb = string.Empty;
    private int _version;
    private int _drawnVersion;
    private IFileSystem? _fileSystem;
    private IReadOnlyList<Url> _roots = [];
    private Url? _manifestHint;

    public HashManifestDialog(IThemeService themeService)
        : base(nameof(HashManifestDialog), themeService)
    {
        Title = UiText.Get("Dialog.HashManifest", "Hash manifest");
        _comboAlgorithm = new ComboBox<ChecksumAlgorithm>(themeService) { DisplaySelector = ChecksumService.DisplayName };
        _comboAlgorithm.SetItems([ChecksumAlgorithm.Sha256, ChecksumAlgorithm.Sha512, ChecksumAlgorithm.Sha1, ChecksumAlgorithm.Md5]);
        _form = new FormPanel(themeService);
        _form.AddField(UiText.Get("Manifest.Field.Algorithm", "Algorithm"), _comboAlgorithm);
        _list = new ResultList(themeService);

        AddControl(_form);
        AddControl(_list);
    }

    /// <summary>The folder the manifest lives in and covers.</summary>
    public Url? BaseFolder { get; private set; }

    /// <summary>The rows the manifest is made of; empty means the whole folder tree.</summary>
    public IReadOnlyList<Url> Roots => _roots;

    /// <summary>The run started last; nothing waits on it — it exists so a test can.</summary>
    public Task RunTask { get; private set; } = Task.CompletedTask;

    /// <summary>The manifest built by the last create, once it is written (or refused).</summary>
    public ManifestCreated? Created { get; private set; }

    /// <summary>What the last write did.</summary>
    public ManifestWriteOutcome? WriteOutcome { get; private set; }

    /// <summary>What the last verify found.</summary>
    public ManifestVerified? Verified { get; private set; }

    public string Status => _status;

    /// <summary>The rows listed now: the failures of a create, or every verified row.</summary>
    public IReadOnlyList<ManifestItem> Items => _list.Items;

    public bool IsRunning => !RunTask.IsCompleted;

    public ChecksumAlgorithm Algorithm
    {
        get => _comboAlgorithm.SelectedItem;
        set => _comboAlgorithm.SelectedIndex = _comboAlgorithm.Items.ToList().IndexOf(value);
    }

    /// <summary>
    /// Sets what the dialog acts on; call before showing it. <paramref name="manifestHint"/> is a manifest the
    /// cursor was on (verify uses it); without one, verify looks for <c>SHA256SUMS</c> and its kin in the folder.
    /// </summary>
    public void Load(Url baseFolder, IReadOnlyList<Url> roots, Url? manifestHint, IFileSystem? fileSystem)
    {
        _cancellation?.Cancel();
        BaseFolder = baseFolder;
        _roots = roots;
        _manifestHint = manifestHint;
        _fileSystem = fileSystem;
        Created = null;
        WriteOutcome = null;
        Verified = null;
        _list.Clear();
        _progress = null;
        SetStatus(UiText.Get("Manifest.Status.Ready", "F2 creates a manifest, F3 verifies one."), false);
    }

    private static string Say(string key, string english, params object[] args) => string.Format(UiText.Get(key, english), args);

    private void SetStatus(string status, bool isError)
    {
        _status = status;
        _statusIsError = isError;
        Interlocked.Increment(ref _version);
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            int width = Math.Clamp((int)Parent.ViewSize.Width - 8, 60, 110);
            int height = Math.Clamp((int)Parent.ViewSize.Height - 4, 16, 34);
            CenterInParent(new Size(width, height));
        }

        int inner = (int)Math.Max(1, ViewSize.Width - 4);
        _form.Position = new Position(2, 3);
        _form.ViewSize = new Size(inner, 1);
        _list.Position = new Position(1, 5);
        _list.ViewSize = new Size(Math.Max(1, ViewSize.Width - 2), Math.Max(1, ViewSize.Height - 8));
        base.OnInitializeLayout();
    }

    /// <summary>The run lands on a worker: the idle pass repaints when it has moved.</summary>
    public override void OnIdle()
    {
        base.OnIdle();
        int version = Volatile.Read(ref _version);
        if (version != _drawnVersion)
        {
            _drawnVersion = version;
            StateHasChanged();
        }
    }

    #region Running
    private bool TryBegin(out CancellationTokenSource cancellation, out Url? baseFolder, out IFileSystem? fileSystem)
    {
        cancellation = new CancellationTokenSource();
        baseFolder = BaseFolder;
        fileSystem = _fileSystem;
        if (IsRunning)
        {
            return false;
        }

        if (baseFolder is null)
        {
            SetStatus(Say("Manifest.Status.NoFolder", "There is no folder to work on."), true);
            return false;
        }

        _cancellation = cancellation;
        _progress = null;
        _list.Clear();
        Interlocked.Increment(ref _version);
        return true;
    }

    /// <summary>F2: hashes the rows (or the whole folder tree) and writes the manifest — unless it would replace one that says something else.</summary>
    public void StartCreate()
    {
        if (!TryBegin(out CancellationTokenSource cancellation, out Url? baseFolder, out IFileSystem? fileSystem))
        {
            return;
        }

        ChecksumAlgorithm algorithm = Algorithm;
        IReadOnlyList<Url> roots = _roots;
        _verb = UiText.Get("Manifest.Verb.Creating", "Creating");
        Created = null;
        WriteOutcome = null;
        Verified = null;
        SetStatus(Say("Manifest.Status.Listing", "Listing the files…"), false);
        RunTask = Task.Run(async () =>
        {
            try
            {
                SyncProgress progress = new(value => { _progress = value; Interlocked.Increment(ref _version); });
                ManifestCreated created = await HashManifest.CreateAsync(baseFolder!, roots, fileSystem!, algorithm, progress, cancellation.Token);
                Created = created;
                _list.SetItems(created.Failures);
                if (created.Failures.Count > 0)
                {
                    SetStatus(Say("Manifest.Status.CreateFailures", "Not written: {0} file(s) could not be read.", created.Failures.Count), true);
                }
                else if (created.Files == 0)
                {
                    SetStatus(Say("Manifest.Status.NoFiles", "There is nothing to hash here."), true);
                }
                else
                {
                    (ManifestWriteOutcome outcome, _, string detail) = HashManifest.Write(baseFolder!, created);
                    WriteOutcome = outcome;
                    switch (outcome)
                    {
                        case ManifestWriteOutcome.Saved:
                            SetStatus(Say("Manifest.Status.Saved", "Saved {0}: {1} file(s).", created.FileName, created.Files), false);
                            break;
                        case ManifestWriteOutcome.AlreadySaysSo:
                            SetStatus(Say("Manifest.Status.AlreadySaysSo", "{0} already says so.", created.FileName), false);
                            break;
                        case ManifestWriteOutcome.SaysSomethingElse:
                            SetStatus(Say("Manifest.Status.SaysSomethingElse", "{0} exists and says something else — not overwritten.", created.FileName), true);
                            break;
                        case ManifestWriteOutcome.NotLocal:
                            SetStatus(UiText.Get("Manifest.Status.NotLocal", "A manifest can only be written into a folder on the local disk."), true);
                            break;
                        default:
                            SetStatus(Say("Manifest.Status.WriteFailed", "Could not save {0}: {1}", created.FileName, detail), true);
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiText.Get("Manifest.Status.StoppedCreate", "Stopped — nothing was written."), false);
            }
            catch (Exception exception)
            {
                SetStatus(Say("Manifest.Status.Failed", "Failed: {0}", exception.Message), true);
            }
            finally
            {
                _progress = null;
                StateHasChanged();
            }
        });
    }

    /// <summary>F3: checks the folder against a manifest and lists every row as OK, changed, missing or extra.</summary>
    public void StartVerify()
    {
        if (!TryBegin(out CancellationTokenSource cancellation, out Url? baseFolder, out IFileSystem? fileSystem))
        {
            return;
        }

        Url? hint = _manifestHint;
        _verb = UiText.Get("Manifest.Verb.Verifying", "Verifying");
        Created = null;
        WriteOutcome = null;
        Verified = null;
        SetStatus(Say("Manifest.Status.Listing", "Listing the files…"), false);
        RunTask = Task.Run(async () =>
        {
            try
            {
                Url? manifest = hint ?? FindManifest(baseFolder!, fileSystem!);
                if (manifest is null)
                {
                    SetStatus(UiText.Get("Manifest.Status.NoManifest", "No manifest here: put the cursor on one (SHA256SUMS…) or create one with F2."), true);
                    return;
                }

                SyncProgress progress = new(value => { _progress = value; Interlocked.Increment(ref _version); });
                ManifestVerified verified = await HashManifest.VerifyAsync(manifest, baseFolder!, fileSystem!, progress, item => _list.Add(item), cancellation.Token);
                Verified = verified;
                SetStatus(
                    Say("Manifest.Summary", "{0}: {1} OK, {2} changed, {3} missing, {4} extra, {5} unreadable.",
                        FocusedEntry.NameOf(manifest),
                        verified.Count(ManifestStatus.Ok), verified.Count(ManifestStatus.Changed), verified.Count(ManifestStatus.Missing),
                        verified.Count(ManifestStatus.Extra), verified.Count(ManifestStatus.Unreadable)),
                    !verified.IsClean && verified.Items.Any(i => i.Status != ManifestStatus.Extra));
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiText.Get("Manifest.Status.Stopped", "Stopped."), false);
            }
            catch (Exception exception)
            {
                SetStatus(Say("Manifest.Status.Failed", "Failed: {0}", exception.Message), true);
            }
            finally
            {
                _progress = null;
                StateHasChanged();
            }
        });
    }

    /// <summary>The conventional manifest in the folder, if there is one: <c>SHA256SUMS</c> first, then the others.</summary>
    private static Url? FindManifest(Url folder, IFileSystem fileSystem)
    {
        foreach (ChecksumAlgorithm algorithm in ChecksumService.Algorithms.Reverse())
        {
            foreach (string name in new[] { HashManifest.FileName(algorithm), HashManifest.FileName(algorithm) + ".txt" })
            {
                Url candidate = new(UrlPath.Child(folder.FullUrl, name, isFolder: false));
                try
                {
                    if (candidate.ContainerType == ContainerType.FileSystem ? File.Exists(candidate.FullName) : fileSystem.ExistsFile(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                }
            }
        }

        return null;
    }

    private sealed class SyncProgress(Action<ManifestProgress> report) : IProgress<ManifestProgress>
    {
        public void Report(ManifestProgress value) => report(value);
    }

    /// <summary>Stops a running create or verify; the dialog stays up.</summary>
    public bool Cancel()
    {
        if (!IsRunning)
        {
            return false;
        }

        _cancellation?.Cancel();
        return true;
    }

    public override void Close()
    {
        // Nobody is left to read the result, and over ssh the read is a transfer.
        _cancellation?.Cancel();
        base.Close();
    }
    #endregion

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("F2", UiText.Get("Manifest.Hint.Create", "Create the manifest"));
        yield return ("F3", UiText.Get("Manifest.Hint.Verify", "Verify against a manifest"));
        yield return ("Esc", UiText.Get("Manifest.Hint.Stop", "Stop a running check, or close"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift;
        if (plain && keyEvent.ConsoleKey == ConsoleKey.F2)
        {
            StartCreate();
            return true;
        }

        if (plain && keyEvent.ConsoleKey == ConsoleKey.F3)
        {
            StartVerify();
            return true;
        }

        if (plain && keyEvent.ConsoleKey == ConsoleKey.Escape && Cancel())
        {
            return true;
        }

        if (plain)
        {
            int step = keyEvent.ConsoleKey switch
            {
                ConsoleKey.UpArrow when !_comboAlgorithm.IsFocused => -1,
                ConsoleKey.DownArrow when !_comboAlgorithm.IsFocused => 1,
                ConsoleKey.PageUp => -_list.PageSize,
                ConsoleKey.PageDown => _list.PageSize,
                ConsoleKey.Home when !_comboAlgorithm.IsFocused => -int.MaxValue / 2,
                ConsoleKey.End when !_comboAlgorithm.IsFocused => int.MaxValue / 2,
                _ => 0,
            };
            if (step != 0)
            {
                _list.Scroll(step);
                StateHasChanged();
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            _list.Scroll(mouseEvent.WheelDelta > 0 ? -3 : 3);
            StateHasChanged();
            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>The words for a row's status.</summary>
    internal static string DescribeStatus(ManifestStatus status) => status switch
    {
        ManifestStatus.Ok => UiText.Get("Manifest.Kind.Ok", "OK"),
        ManifestStatus.Changed => UiText.Get("Manifest.Kind.Changed", "changed"),
        ManifestStatus.Missing => UiText.Get("Manifest.Kind.Missing", "missing"),
        ManifestStatus.Extra => UiText.Get("Manifest.Kind.Extra", "extra"),
        _ => UiText.Get("Manifest.Kind.Unreadable", "unreadable"),
    };

    /// <summary>What is being worked on, as the dialog's second line says it.</summary>
    internal string Scope() => _roots.Count == 0
        ? UiText.Get("Manifest.Scope.Whole", "Covers: the whole folder tree")
        : Say("Manifest.Scope.Rows", "Covers: {0} selected row(s) and everything under them", _roots.Count);

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        int width = (int)ViewSize.Width;
        int inner = Math.Max(0, width - 4);

        void Line(int row, string text, Color color) =>
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + 2, AbsolutePosition.Y + row), new Size(inner, 1),
                new ConxelString((text.Shrink(inner) ?? string.Empty).ExpandRight(inner), color, colorScheme.Background));

        Line(1, Say("Manifest.Scope.Folder", "Folder: {0}", BaseFolder?.FullUrl ?? "—"), foreground);
        Line(2, Scope(), foreground);

        var rule = new ConxelString(Border.Horizontal.Repeat(Math.Max(0, width - 2)), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 4), new Size(rule.Count, 1), rule);

        ManifestProgress? progress = _progress;
        string status = progress is null
            ? _status
            : Say("Manifest.Working", "{0}… {1}% ({2} of {3} files)", _verb, progress.Percent, progress.FilesDone, progress.FilesTotal);
        Line((int)ViewSize.Height - 3, status, _statusIsError ? colorScheme.Error : foreground);
        Line((int)ViewSize.Height - 2, string.Empty, foreground);

        string hints = (" " + string.Join("  ", GetKeyHints().Select(h => $"{h.Key}:{h.Description}")) + " ").FitHints(Math.Max(0, width - 4));
        string bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, hints), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }

    /// <summary>The rows, one per line: the status, then the path.</summary>
    private sealed class ResultList(IThemeService themeService) : Control(themeService)
    {
        private readonly Lock _lock = new();
        private readonly List<ManifestItem> _items = [];
        private int _top;

        public int PageSize => Math.Max(1, (int)ViewSize.Height - 1);

        public IReadOnlyList<ManifestItem> Items
        {
            get
            {
                lock (_lock)
                {
                    return [.. _items];
                }
            }
        }

        public void Clear() => SetItems([]);

        public void SetItems(IReadOnlyList<ManifestItem> items)
        {
            lock (_lock)
            {
                _items.Clear();
                _items.AddRange(items);
                _top = 0;
            }

            StateHasChanged();
        }

        public void Add(ManifestItem item)
        {
            lock (_lock)
            {
                _items.Add(item);
            }
        }

        public void Scroll(int step)
        {
            lock (_lock)
            {
                _top = Math.Clamp(_top + step, 0, Math.Max(0, _items.Count - (int)ViewSize.Height));
            }
        }

        public override void Draw(IBuffer buffer)
        {
            ManifestItem[] items;
            lock (_lock)
            {
                items = [.. _items.Skip(_top).Take((int)ViewSize.Height)];
            }

            var colorScheme = ThemeService.CurrentColorScheme;
            int width = (int)ViewSize.Width;
            for (int row = 0; row < (int)ViewSize.Height; row++)
            {
                string text = string.Empty;
                Color color = colorScheme.Foreground;
                if (row < items.Length)
                {
                    ManifestItem item = items[row];
                    text = $" {DescribeStatus(item.Status),-11} {item.Path}{(item.Detail is { Length: > 0 } ? " — " + item.Detail : string.Empty)}";
                    color = item.Status switch
                    {
                        ManifestStatus.Ok => Color.Green,
                        ManifestStatus.Extra => Color.Yellow,
                        _ => colorScheme.Error,
                    };
                }

                // Every cell of the rectangle, every time: a shorter list must not leave old rows behind.
                var line = new ConxelString(text.ExpandRight(width)[..width], color, colorScheme.Background);
                buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(width, 1), line);
            }
        }
    }
}
