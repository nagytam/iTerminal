using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Factories;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Compare;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Shift+F2: compares the folders the two panels show, lists every difference, and marks in each
/// panel what that side has and the other lacks or holds an older copy of — so F5 from either panel
/// copies exactly those across. What differs with no newer side (same date, different bytes; a file
/// against a folder) is listed but marked on neither: which copy should win is the user's call.
///
/// <para><b>F5 in the dialog synchronises</b> what the comparison found (<see cref="FolderSynchronizer"/>,
/// <see cref="FolderSyncTask"/>): both ways with the newer copy winning, or one way with an option to
/// delete what the target has extra. The first F5 shows what would happen, the second does it.</para>
///
/// <para>The comparison runs on a background task — with subfolders or by content it can read a
/// great deal — and the dialog shows how far it has got. Closing it stops the comparison.</para>
/// </summary>
public class CompareFoldersDialog : Dialog
{
    private readonly IContentModelService _contentModelService;
    private readonly FileSystemResolver _resolver;
    private readonly FormPanel _form;
    private readonly ComboBox<CompareMode> _comboMode;
    private readonly CheckBox _checkBoxSubfolders;
    private readonly TaskManager? _taskManager;
    private readonly ProgressWindowFactory? _progressWindowFactory;
    private readonly FormPanel _syncForm;
    private readonly ComboBox<SyncDirection> _comboDirection;
    private readonly CheckBox _checkBoxDeleteExtras;
    private readonly DiffList _list;
    private CancellationTokenSource? _cancellation;
    private volatile string _status = string.Empty;
    private volatile bool _statusIsError;
    private volatile string _progress = string.Empty;
    private volatile ComparedFolders? _compared;
    private SyncPlan? _pendingPlan;

    /// <summary>What the last comparison looked at — everything a synchronisation needs to act on it.</summary>
    private sealed record ComparedFolders(Url Left, IFileSystem LeftFileSystem, Url Right, IFileSystem RightFileSystem);

    public CompareFoldersDialog(
        IContentModelService contentModelService,
        FileSystemResolver resolver,
        IThemeService themeService,
        TaskManager? taskManager = null,
        ProgressWindowFactory? progressWindowFactory = null)
        : base(nameof(CompareFoldersDialog), themeService)
    {
        _contentModelService = contentModelService;
        _resolver = resolver;
        _taskManager = taskManager;
        _progressWindowFactory = progressWindowFactory;
        Title = iConsole.Localization.UiText.Get("Dialog.CompareAndSynchroniseFolders", "Compare and synchronise folders");

        _comboMode = new ComboBox<CompareMode>(themeService) { DisplaySelector = DescribeMode };
        _comboMode.SetItems([CompareMode.SizeAndDate, CompareMode.SizeOnly, CompareMode.Content]);
        _form = new FormPanel(themeService);
        _form.AddField("Compare", _comboMode);
        _checkBoxSubfolders = new CheckBox(themeService)
        {
            Label = UiText.Get("Field.IncludeSubfolders", "Include subfolders"),
            IsChecked = false,
            HotKeyChar = iConsole.Localization.HotKeys.VisibleIn(iConsole.Localization.UiText.Get("Field.IncludeSubfolders", "Include subfolders"), 'S'),
        };
        _comboDirection = new ComboBox<SyncDirection>(themeService) { DisplaySelector = DescribeDirection };
        _comboDirection.SetItems([SyncDirection.Both, SyncDirection.LeftToRight, SyncDirection.RightToLeft]);
        _syncForm = new FormPanel(themeService);
        _syncForm.AddField("Sync (F5)", _comboDirection);
        _checkBoxDeleteExtras = new CheckBox(themeService)
        {
            Label = UiText.Get("Field.DeleteTargetLacksSource", "Delete what the target has and the source lacks (one-way only)"),
            IsChecked = false,
            HotKeyChar = iConsole.Localization.HotKeys.VisibleIn(iConsole.Localization.UiText.Get("Field.DeleteTargetLacksSource", "Delete what the target has and the source lacks (one-way only)"), 'D'),
        };
        _list = new DiffList(themeService);

        AddControl(_form);
        AddControl(_checkBoxSubfolders);
        AddControl(_syncForm);
        AddControl(_checkBoxDeleteExtras);
        AddControl(_list);
        AddCommand(new CompareCommand());
    }

    private static string DescribeDirection(SyncDirection direction) => direction switch
    {
        SyncDirection.Both => "Both ways — newer copy wins",
        SyncDirection.LeftToRight => "Left to right — left is the source",
        SyncDirection.RightToLeft => "Right to left — right is the source",
        _ => direction.ToString(),
    };

    public SyncDirection Direction
    {
        get => _comboDirection.SelectedItem;
        set => _comboDirection.SelectedIndex = _comboDirection.Items.ToList().IndexOf(value);
    }

    public bool DeleteExtras
    {
        get => _checkBoxDeleteExtras.IsChecked;
        set => _checkBoxDeleteExtras.IsChecked = value;
    }

    /// <summary>The synchronisation F5 has proposed and is waiting to have confirmed, if any.</summary>
    public SyncPlan? PendingPlan => _pendingPlan;

    /// <summary>The synchronisation started last — nothing waits on it; it exists so a test can.</summary>
    public FolderSyncTask? SyncTask { get; private set; }

    private static string DescribeMode(CompareMode mode) => mode switch
    {
        CompareMode.SizeAndDate => "Name, size and date",
        CompareMode.SizeOnly => "Name and size (dates ignored)",
        CompareMode.Content => "Name and content (reads both files)",
        _ => mode.ToString(),
    };

    public CompareMode Mode
    {
        get => _comboMode.SelectedItem;
        set => _comboMode.SelectedIndex = _comboMode.Items.ToList().IndexOf(value);
    }

    public bool IncludeSubfolders
    {
        get => _checkBoxSubfolders.IsChecked;
        set => _checkBoxSubfolders.IsChecked = value;
    }

    /// <summary>The comparison started last; nothing waits on it — it exists so a test can.</summary>
    public Task CompareTask { get; private set; } = Task.CompletedTask;

    public CompareResult? Result { get; private set; }

    public string Status => _status;

    /// <summary>How many rows the last comparison marked in each panel.</summary>
    public (int Left, int Right) Marked { get; private set; }

    private IContentModel? LeftModel => _contentModelService.ContentModels.FirstOrDefault(m => m.Name == ContentModelService.Left);
    private IContentModel? RightModel => _contentModelService.ContentModels.FirstOrDefault(m => m.Name == ContentModelService.Right);

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        Result = null;
        _compared = null;
        _pendingPlan = null;
        _list.SetItems([]);
        _status = Say("Folders.Status.EnterCompares", "Enter compares.");
        _statusIsError = false;
        _progress = string.Empty;
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            var width = Math.Clamp((int)Parent.ViewSize.Width - 8, 60, 110);
            var height = Math.Clamp((int)Parent.ViewSize.Height - 4, 19, 36);
            CenterInParent(new Size(width, height));
        }

        var inner = Math.Max(1, ViewSize.Width - 4);
        _form.Position = new Position(2, 4);
        _form.ViewSize = new Size(inner, 1);
        _checkBoxSubfolders.Position = new Position(2, 5);
        _checkBoxSubfolders.ViewSize = new Size(inner, 1);
        _syncForm.Position = new Position(2, 6);
        _syncForm.ViewSize = new Size(inner, 1);
        _checkBoxDeleteExtras.Position = new Position(2, 7);
        _checkBoxDeleteExtras.ViewSize = new Size(inner, 1);
        _list.Position = new Position(1, 9);
        _list.ViewSize = new Size(Math.Max(1, ViewSize.Width - 2), Math.Max(1, ViewSize.Height - 12));
        base.OnInitializeLayout();
    }

    /// <summary>Starts a comparison of what the two panels show now, unless one is running.</summary>
    public void StartCompare()
    {
        if (!CompareTask.IsCompleted)
        {
            return;
        }

        var left = LeftModel;
        var right = RightModel;
        if (left?.Url is null || right?.Url is null)
        {
            SetStatus(Say("Folders.Status.BothFolders", "Both panels must show a folder."), isError: true);
            return;
        }

        var leftFolder = left.Url;
        var rightFolder = right.Url;
        IFileSystem leftFileSystem, rightFileSystem;
        try
        {
            leftFileSystem = _resolver.Resolve(leftFolder, left);
            rightFileSystem = _resolver.Resolve(rightFolder, right);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
            return;
        }

        var options = new CompareOptions(Mode, IncludeSubfolders);
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _cancellation, cancellation)?.Cancel();
        Result = null;
        _compared = null;
        _pendingPlan = null;
        _list.SetItems([]);
        SetStatus(Say("Folders.Status.Comparing", "Comparing…"), isError: false);
        var progress = new Helper.SynchronousProgress<string>(path => _progress = path);

        CompareTask = Task.Run(async () =>
        {
            try
            {
                var result = await FolderComparer.CompareAsync(leftFolder, leftFileSystem, rightFolder, rightFileSystem, options, progress, cancellation.Token);
                Result = result;
                _compared = new ComparedFolders(leftFolder, leftFileSystem, rightFolder, rightFileSystem);
                _list.SetItems([.. result.Differences]);
                Marked = ApplyMarks(result, left, leftFolder, right, rightFolder);
                var marked = Marked.Left + Marked.Right == 0
                    ? string.Empty
                    : $" Marked {Marked.Left} on the left and {Marked.Right} on the right — F5 here synchronises; Esc, then F5 in a panel, copies just the marked rows.";
                SetStatus(result.Summary() + marked, isError: false);
            }
            catch (OperationCanceledException)
            {
                SetStatus(Say("Folders.Status.Stopped", "Comparison stopped."), isError: false);
            }
            catch (Exception exception)
            {
                SetStatus(Say("Folders.Status.CouldNotCompare", "Could not compare: {0}", exception.Message), isError: true);
            }
            finally
            {
                _progress = string.Empty;
                StateHasChanged();
            }
        });
    }

    /// <summary>
    /// F5: proposes what a synchronisation would do and waits for a second F5 — it can delete, and
    /// "recoverable by default" includes not doing it on one keystroke. Any other key withdraws it.
    /// </summary>
    public async Task RequestSyncAsync()
    {
        if (_compared is null || Result is null)
        {
            SetStatus(Say("Folders.Status.CompareFirst", "Compare first (Enter), then F5 synchronises what it found."), isError: true);
            return;
        }

        if (!CompareTask.IsCompleted)
        {
            return;
        }

        if (_pendingPlan is { } pending)
        {
            _pendingPlan = null;
            await RunSyncAsync(pending);
            return;
        }

        var plan = FolderSynchronizer.Plan(Result, new SyncOptions(Direction, DeleteExtras));
        if (plan.IsEmpty)
        {
            SetStatus(plan.Summary(), isError: false);
            return;
        }

        if (_compared.LeftFileSystem.IsReadOnly && (plan.CopiesToLeft > 0 || plan.Actions.Any(a => a.Kind == SyncActionKind.DeleteLeft))
            || _compared.RightFileSystem.IsReadOnly && (plan.CopiesToRight > 0 || plan.Actions.Any(a => a.Kind == SyncActionKind.DeleteRight)))
        {
            SetStatus(Say("Folders.Status.ReadOnly", "One of the folders is read-only, so this synchronisation cannot be carried out."), isError: true);
            return;
        }

        _pendingPlan = plan;
        var recycle = plan.Deletes > 0 ? " Deletions go to the recycle bin where there is one." : string.Empty;
        SetStatus(Say("Folders.Status.SyncAgain", "{0} F5 again to do it, any other key to cancel.", $"{plan.Summary()}{recycle}"), isError: plan.Deletes > 0);
    }

    private async Task RunSyncAsync(SyncPlan plan)
    {
        var compared = _compared;
        if (compared is null || _taskManager is null || _progressWindowFactory is null)
        {
            SetStatus(Say("Folders.Status.NotHere", "Synchronising is not available here."), isError: true);
            return;
        }

        try
        {
            var task = new FolderSyncTask(plan, compared.Left, compared.LeftFileSystem, compared.Right, compared.RightFileSystem);
            SyncTask = task;

            // Before the task is queued: a small synchronisation is regularly over before Add returns.
            PanelRefreshHelper.RefreshFoldersWhenFinished(task, _contentModelService, compared.Left, compared.Right);
            await _taskManager.Add(task);
            await ShowPopUpWindowAsync(_progressWindowFactory.Create(task));
            Close();
        }
        catch (Exception exception)
        {
            SetStatus(Say("Folders.Status.CouldNotSync", "Could not synchronise: {0}", exception.Message), isError: true);
        }
    }

    /// <summary>A text in the chosen language, with its arguments filled in.</summary>
    private static string Say(string key, string english, params object[] args) => string.Format(UiText.Get(key, english), args);

    private void SetStatus(string status, bool isError)
    {
        _status = status;
        _statusIsError = isError;
        StateHasChanged();
    }

    /// <summary>
    /// Marks, in each panel, the top-level rows that side should copy across: what only it has, and
    /// what it has newer. Only when the panel still shows the folder that was compared — a panel moved
    /// on in the meantime would have rows of the same names marked in a different folder.
    /// </summary>
    internal static (int Left, int Right) ApplyMarks(CompareResult result, IContentModel left, Url leftFolder, IContentModel right, Url rightFolder) =>
        (Mark(result, left, leftFolder, DiffKind.LeftOnly, DiffKind.LeftNewer),
         Mark(result, right, rightFolder, DiffKind.RightOnly, DiffKind.RightNewer));

    private static int Mark(CompareResult result, IContentModel model, Url folder, DiffKind only, DiffKind newer)
    {
        if (model is not IMarkableContentModel { CanMark: true } marks
            || !ContentModelBase.IsSameLocation(model.Url?.FullUrl, folder.FullUrl))
        {
            return 0;
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var wanted = result.Items
            .Where(i => i.IsTopLevel && (i.Kind == only || i.Kind == newer))
            .Select(i => i.Name)
            .ToHashSet(comparer);

        marks.ClearMarks();
        var count = 0;
        foreach (var entry in model.FilteredEntries)
        {
            if (!FocusedEntry.IsParentEntry(entry) && wanted.Contains(FocusedEntry.NameOf(entry.Url)) && marks.SetMark(entry, true))
            {
                count++;
            }
        }

        model.Update();
        return count;
    }

    public override void Close()
    {
        Interlocked.Exchange(ref _cancellation, null)?.Cancel();
        base.Close();
    }

    /// <summary>
    /// Enter compares wherever focus is — claimed ahead of the controls, because the compare-mode list
    /// takes focus first and would answer Enter by opening itself (Space, F4 or Alt+Down still open
    /// it). ↑/↓ and PgUp/PgDn scroll the differences, unless that list has focus and wants the arrows.
    /// </summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("DialogHint.CompareTwoPanels", "Compare the two panels"));
        yield return ("F5", UiText.Get("DialogHint.SynchroniseShowsPlan", "Synchronise: shows the plan first, any key but Esc cancels it"));
        yield return ("Up/Down", UiText.Get("DialogHint.ScrollDifferences", "Scroll the differences"));
        yield return ("PageUp/PageDown", UiText.Get("DialogHint.ScrollDifferencesPage", "Scroll the differences a page at a time"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isSyncKey = keyEvent.ConsoleKey == ConsoleKey.F5 && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift;
        if (isSyncKey)
        {
            await RequestSyncAsync();
            return true;
        }

        if (_pendingPlan is not null && keyEvent.ConsoleKey != ConsoleKey.Escape)
        {
            _pendingPlan = null;
            SetStatus(Say("Folders.Status.SyncCancelled", "Synchronisation cancelled — nothing was changed."), isError: false);
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift && !_comboMode.IsOpen)
        {
            StartCompare();
            return true;
        }

        if (!keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            // The compare-mode list wants the arrows, Home and End while it has focus; it has no use
            // for the page keys, so those scroll the differences from anywhere.
            var step = keyEvent.ConsoleKey switch
            {
                ConsoleKey.UpArrow when !_comboMode.IsFocused => -1,
                ConsoleKey.DownArrow when !_comboMode.IsFocused => 1,
                ConsoleKey.PageUp => -_list.PageSize,
                ConsoleKey.PageDown => _list.PageSize,
                ConsoleKey.Home when !_comboMode.IsFocused => -int.MaxValue / 2,
                ConsoleKey.End when !_comboMode.IsFocused => int.MaxValue / 2,
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

    /// <summary>The wheel scrolls the differences, three rows a notch, wherever in the dialog the pointer is.</summary>
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

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var inner = Math.Max(0, width - 4);

        void Line(int row, string text, Color color) =>
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + 2, AbsolutePosition.Y + row), new Size(inner, 1),
                new ConxelString((text.Shrink(inner) ?? string.Empty).ExpandRight(inner), color, colorScheme.Background));

        Line(1, "Left:  " + (LeftModel?.Url?.FullUrl ?? "—"), foreground);
        Line(2, "Right: " + (RightModel?.Url?.FullUrl ?? "—"), foreground);

        var rule = new ConxelString(Border.Horizontal.Repeat(Math.Max(0, width - 2)), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 8), new Size(rule.Count, 1), rule);

        var status = _progress.Length > 0 ? $"Comparing… {_progress}" : _status;
        Line((int)ViewSize.Height - 3, status, _statusIsError ? colorScheme.Error : foreground);
        Line((int)ViewSize.Height - 2, string.Empty, foreground);

        var hints = " Enter:compare  F5:synchronise  ↑↓/wheel:scroll  Esc:close ".FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, hints), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }

    internal static string DescribeKind(DiffKind kind) => kind switch
    {
        DiffKind.LeftOnly => "◀ only left",
        DiffKind.RightOnly => "only right ▶",
        DiffKind.LeftNewer => "◀ newer left",
        DiffKind.RightNewer => "newer right ▶",
        DiffKind.Different => "≠ different",
        _ => "= identical",
    };

    /// <summary>Enter: compare (again).</summary>
    private sealed class CompareCommand : Command
    {
        public override string Title => "Compare";
        public override string Description => "Compare the two panels' folders.";
        public override Key? Key { get; init; } = new Key(ConsoleKey.Enter, false, false, false);

        public override Task ExecuteAsync(object? args)
        {
            (Control as CompareFoldersDialog ?? Control?.Parent as CompareFoldersDialog)?.StartCompare();
            return Task.CompletedTask;
        }
    }

    /// <summary>The differences, one per row: what kind, then the path; folders end in '/'.</summary>
    private sealed class DiffList(IThemeService themeService) : Control(themeService)
    {
        private readonly Lock _lock = new();
        private IReadOnlyList<DiffItem> _items = [];
        private int _top;

        public int PageSize => Math.Max(1, (int)ViewSize.Height - 1);

        public void SetItems(IReadOnlyList<DiffItem> items)
        {
            lock (_lock)
            {
                _items = items;
                _top = 0;
            }

            StateHasChanged();
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
            IReadOnlyList<DiffItem> items;
            int top;
            lock (_lock)
            {
                items = _items;
                top = _top;
            }

            var colorScheme = ThemeService.CurrentColorScheme;
            var width = (int)ViewSize.Width;
            for (var row = 0; row < (int)ViewSize.Height; row++)
            {
                var index = top + row;
                var text = string.Empty;
                var color = colorScheme.Foreground;
                if (index < items.Count)
                {
                    var item = items[index];
                    text = $" {DescribeKind(item.Kind),-14} {item.Path}{(item.IsFolder ? "/" : string.Empty)}";
                    color = item.Kind switch
                    {
                        DiffKind.LeftOnly or DiffKind.LeftNewer => Color.Cyan,
                        DiffKind.RightOnly or DiffKind.RightNewer => Color.Yellow,
                        DiffKind.Different => colorScheme.Error,
                        _ => colorScheme.Foreground,
                    };
                }

                // Every cell of the rectangle, every time: a shorter list must not leave old rows behind.
                var line = new ConxelString(text.ExpandRight(width)[..width], color, colorScheme.Background);
                buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(width, 1), line);
            }
        }
    }
}
