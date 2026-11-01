using iConsole.Localization;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Layouts;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Factories;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Compare;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Synchronises the two panels' folders. It compares them, lists what it would do before it does any of it, and runs the
/// plan when asked. The options are the direction (both ways with the newer copy winning, or one way), whether subfolders are
/// compared, and whether files only the target has are deleted (they go to the recycle bin where the drive has one).
///
/// <para>Keys: <c>b</c> direction, <c>s</c> subfolders, <c>x</c> delete extras, <c>p</c> preview, Enter runs the previewed plan
/// (with deletions, a second Enter), Esc closes. A change to an option throws the preview away.</para>
/// </summary>
public class SynchronizeFoldersDialog : Dialog
{
    private const string CloseActionView = " [X]";

    private readonly IContentModelService _contentModelService;
    private readonly FileSystemResolver _resolver;
    private readonly TaskManager? _taskManager;
    private readonly ProgressWindowFactory? _progressWindowFactory;
    private readonly Label _optionsLabel;
    private readonly ListBox<string> _listBox;
    private readonly ConcurrentObservableCollection<string> _rows = [];
    private readonly Label _statusLabel;

    private SyncDirection _direction = SyncDirection.Both;
    private bool _subfolders = true;
    private bool _deleteExtras;

    // The preview, once one has been made. Written by the background work, read on the idle pass.
    private volatile Preview? _ready;
    private volatile string? _failure;
    private volatile string _progress = string.Empty;
    private Task? _work;
    private Preview? _shown;
    private bool _confirmingDeletes;

    private sealed record Preview(Url Left, IFileSystem LeftFileSystem, Url Right, IFileSystem RightFileSystem, SyncPlan Plan);

    public SynchronizeFoldersDialog(
        IContentModelService contentModelService,
        FileSystemResolver resolver,
        IThemeService themeService,
        TaskManager? taskManager = null,
        ProgressWindowFactory? progressWindowFactory = null)
        : base(nameof(SynchronizeFoldersDialog), themeService)
    {
        _contentModelService = contentModelService;
        _resolver = resolver;
        _taskManager = taskManager;
        _progressWindowFactory = progressWindowFactory;
        Title = iConsole.Localization.UiText.Get("Dialog.SynchroniseFolders", "Synchronise folders");

        _optionsLabel = new Label(string.Empty, themeService);
        _listBox = new ListBox<string>(new ListBoxModel<string>(_rows), themeService)
        {
            EmptyText = UiText.Get("Empty.Sync", " Press p to compare the two panels and list what would change."),
        };
        _statusLabel = new Label(string.Empty, themeService);
        AddControl(_optionsLabel);
        AddControl(_listBox);
        AddControl(_statusLabel);
        UpdateOptionsLine();
    }

    /// <summary>The direction the next preview uses.</summary>
    public SyncDirection Direction => _direction;

    /// <summary>Whether the next preview compares subfolders too.</summary>
    public bool Subfolders => _subfolders;

    /// <summary>Whether the next preview lists the files only the target has, for deletion.</summary>
    public bool DeleteExtras => _deleteExtras;

    /// <summary>The plan that is shown, or null before a preview.</summary>
    public SyncPlan? ShownPlan => _shown?.Plan;

    /// <summary>What the status line says.</summary>
    public string Status => _statusLabel.Text;

    /// <summary>The background work of the last preview or run, so a test can await it.</summary>
    public Task? WorkTask => _work;

    private LeftRightModels Panels()
    {
        IContentModel? left = _contentModelService.ContentModels.FirstOrDefault(m => m.Name == ContentModelService.Left);
        IContentModel? right = _contentModelService.ContentModels.FirstOrDefault(m => m.Name == ContentModelService.Right);
        return new LeftRightModels(left, right);
    }

    private sealed record LeftRightModels(IContentModel? Left, IContentModel? Right);

    /// <summary>Compares the two panels' folders and plans the synchronisation. The plan is shown on the next idle pass.</summary>
    public void StartPreview()
    {
        if (IsBusy)
        {
            return;
        }

        (IContentModel? leftModel, IContentModel? rightModel) = Panels();
        if (leftModel?.Url is null || rightModel?.Url is null)
        {
            SetStatus(Say("Folders.Status.BothFolders", "Both panels must show a folder."), isError: true);
            return;
        }

        IFileSystem leftFileSystem, rightFileSystem;
        try
        {
            leftFileSystem = _resolver.Resolve(leftModel.Url, leftModel);
            rightFileSystem = _resolver.Resolve(rightModel.Url, rightModel);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
            return;
        }

        Url left = leftModel.Url;
        Url right = rightModel.Url;
        SyncDirection direction = _direction;
        bool deleteExtras = _deleteExtras;
        var options = new CompareOptions(CompareMode.SizeAndDate, _subfolders);
        var progress = new SynchronousProgress<string>(path => _progress = path);
        _shown = null;
        _rows.ReplaceAll([]);
        _confirmingDeletes = false;
        SetStatus(Say("Folders.Status.Comparing", "Comparing…"), isError: false);

        _work = Task.Run(async () =>
        {
            try
            {
                CompareResult result = await FolderComparer.CompareAsync(left, leftFileSystem, right, rightFileSystem, options, progress, CancellationToken.None);
                SyncPlan plan = FolderSynchronizer.Plan(result, new SyncOptions(direction, deleteExtras));
                _ready = new Preview(left, leftFileSystem, right, rightFileSystem, plan);
            }
            catch (Exception exception)
            {
                _failure = exception.Message;
            }
            finally
            {
                _progress = string.Empty;
            }
        });
    }

    /// <summary>Runs the shown plan. With deletions in it, the first call only asks for the second.</summary>
    public async Task RunAsync()
    {
        if (_shown is not { } preview || IsBusy)
        {
            return;
        }

        if (preview.Plan.IsEmpty)
        {
            SetStatus(Say("Folders.Status.NothingToSync", "Nothing to synchronise: the folders match."), isError: false);
            return;
        }

        if (preview.Plan.Deletes > 0 && !_confirmingDeletes)
        {
            _confirmingDeletes = true;
            SetStatus(Say("Folders.Status.SyncWithRecycle", "{0} Deletions go to the recycle bin where there is one. Enter again to run.", preview.Plan.Summary()), isError: true);
            return;
        }

        if (_taskManager is null || _progressWindowFactory is null)
        {
            SetStatus(Say("Folders.Status.NotHere", "Synchronising is not available here."), isError: true);
            return;
        }

        try
        {
            var task = new FolderSyncTask(preview.Plan, preview.Left, preview.LeftFileSystem, preview.Right, preview.RightFileSystem);
            PanelRefreshHelper.RefreshFoldersWhenFinished(task, _contentModelService, preview.Left, preview.Right);
            await _taskManager.Add(task);
            await ShowPopUpWindowAsync(_progressWindowFactory.Create(task));
            Close();
        }
        catch (Exception exception)
        {
            SetStatus(Say("Folders.Status.CouldNotSync", "Could not synchronise: {0}", exception.Message), isError: true);
        }
    }

    private bool IsBusy => _work is not null && !_work.IsCompleted;

    /// <summary>Changes the direction: both ways, then left to right, then right to left.</summary>
    public void CycleDirection()
    {
        _direction = _direction switch
        {
            SyncDirection.Both => SyncDirection.LeftToRight,
            SyncDirection.LeftToRight => SyncDirection.RightToLeft,
            _ => SyncDirection.Both,
        };
        OptionChanged();
    }

    public void ToggleSubfolders()
    {
        _subfolders = !_subfolders;
        OptionChanged();
    }

    public void ToggleDeleteExtras()
    {
        _deleteExtras = !_deleteExtras;
        OptionChanged();
    }

    private void OptionChanged()
    {
        // The shown plan was made with the old options: it is no longer what would run.
        _shown = null;
        _ready = null;
        _rows.ReplaceAll([]);
        _confirmingDeletes = false;
        UpdateOptionsLine();
        SetStatus(Say("Folders.Status.OptionsChanged", "Options changed. Press p to compare again."), isError: false);
    }

    private static string DescribeDirection(SyncDirection direction) => direction switch
    {
        SyncDirection.Both => UiText.Get("Sync.BothWays", "both ways, newer wins"),
        SyncDirection.LeftToRight => UiText.Get("Sync.LeftToRight", "left to right"),
        _ => UiText.Get("Sync.RightToLeft", "right to left"),
    };

    private void UpdateOptionsLine()
    {
        _optionsLabel.Text = string.Format(UiText.Get("Sync.Options", " Direction: {0}   Subfolders: {1}   Delete extras: {2}"), DescribeDirection(_direction), _subfolders ? UiText.Get("Sync.Yes", "yes") : UiText.Get("Sync.No", "no"), _deleteExtras ? UiText.Get("Sync.Yes", "yes") : UiText.Get("Sync.No", "no"));
    }

    /// <summary>A text in the chosen language, with its arguments filled in.</summary>
    private static string Say(string key, string english, params object[] args) => string.Format(UiText.Get(key, english), args);

    private void SetStatus(string text, bool isError)
    {
        _statusLabel.Text = " " + text;
        _statusIsError = isError;
        StateHasChanged();
    }

    private bool _statusIsError;

    /// <summary>
    /// Shows the finished preview, or the failure, on the thread that owns the controls. The list is filled before the
    /// children's idle pass, so the rows are current in this same pass.
    /// </summary>
    public override void OnIdle()
    {
        if (_failure is { } failure)
        {
            _failure = null;
            SetStatus(Say("Folders.Status.CouldNotCompare", "Could not compare: {0}", failure), isError: true);
        }

        if (_ready is { } ready && !IsBusy)
        {
            _ready = null;
            _shown = ready;
            _rows.ReplaceAll([.. ready.Plan.Actions.Select(a => a.Describe())]);
            SetStatus(ready.Plan.IsEmpty ? Say("Folders.Status.Match", "The folders match: nothing to synchronise.") : Say("Folders.Status.EnterRuns", "{0} Enter runs it.", ready.Plan.Summary()), isError: false);
        }
        else if (IsBusy && _progress.Length > 0)
        {
            _statusLabel.Text = string.Format(UiText.Get("Msg.ComparingProgress", " Comparing… {0}"), _progress);
        }

        base.OnIdle();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", _shown is null ? "Preview what the synchronisation would do" : "Run the previewed synchronisation");
        yield return ("Esc", UiText.Get("DialogHint.CloseWithoutChanging", "Close without changing anything"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down || keyEvent.Control || keyEvent.Alt)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            Close();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter)
        {
            if (_shown is null)
            {
                StartPreview();
            }
            else
            {
                await RunAsync();
            }

            return true;
        }

        switch (char.ToLowerInvariant(keyEvent.KeyChar))
        {
            case 'b':
                CycleDirection();
                return true;
            case 's':
                ToggleSubfolders();
                return true;
            case 'x':
                ToggleDeleteExtras();
                return true;
            case 'p':
                StartPreview();
                return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            var width = Math.Clamp((int)Parent.ViewSize.Width - 8, 60, 110);
            var height = Math.Clamp((int)Parent.ViewSize.Height - 4, 16, 34);
            CenterInParent(new Size(width, height));
        }

        int inner = (int)Math.Max(1, ViewSize.Width - 2);
        _optionsLabel.Position = new Position(1, 1);
        _optionsLabel.ViewSize = new Size(inner, 1);
        _listBox.Position = new Position(1, 3);
        _listBox.ViewSize = new Size(inner, Math.Max(1, ViewSize.Height - 6));
        _statusLabel.Position = new Position(1, Math.Max(0, ViewSize.Height - 2));
        _statusLabel.ViewSize = new Size(inner, 1);
        base.OnInitializeLayout();
        Layout.Center(this);
    }
}
