using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Factories;
using iTerminal.Core.Rename;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Renames the marked entries (or the one under the cursor) by a pattern, with Find and Replace
/// applied after it, and a live preview of every old name and its new one.
///
/// <para>The preview lists at most <see cref="PreviewLimit"/> rows; the count line covers all of
/// them. Planning runs over every row on each change — it is names only, see
/// <see cref="MultiRenamePlanner"/> — and the folder's names are read once, in <see cref="Prepare"/>.
/// The preview list cannot take focus, so Enter always reaches <see cref="MultiRenameCommand"/>.</para>
/// </summary>
public class MultiRenameDialog : FormDialog
{
    /// <summary>The most preview rows drawn. A key handler must return within 200 ms, and a list of thousands of rows is not read anyway.</summary>
    public const int PreviewLimit = 500;

    private readonly ConcurrentObservableCollection<string> _previewRows = [];
    private readonly TextBox _patternBox;
    private readonly TextBox _findBox;
    private readonly TextBox _replaceBox;
    private readonly ListBox<string> _previewList;
    private readonly Label _countLabel;
    private readonly Label _errorLabel;
    private readonly MultiRenameCommand _renameCommand;
    private readonly Button _renameButton;
    private readonly CancelDialogButton _cancelButton;
    private IReadOnlyList<string> _folderNames = [];
    private (string Pattern, string Find, string Replace)? _computedFor;

    public MultiRenameDialog(
        TaskManager taskManager,
        IContentModelService contentModelService,
        ProgressWindowFactory progressWindowFactory,
        IErrorPromptService errorPromptService,
        IConflictPromptService conflictPromptService,
        FileSystemResolver fileSystemResolver,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(MultiRenameDialog), themeService)
    {
        Title = UiText.Get("Dialog.MultiRename", "Multi-rename");

        _renameCommand = new MultiRenameCommand(taskManager, progressWindowFactory, errorPromptService, conflictPromptService, fileSystemResolver, contentModelService);
        AddCommand(_renameCommand);

        _patternBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Label.Pattern", "Pattern"),
            Text = MultiRenamePattern.DefaultPattern,
            IsEnabled = true,
            HotKeyChar = 'P',
        };
        _findBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Label.Find", "Find"),
            Text = string.Empty,
            IsEnabled = true,
            HotKeyChar = 'F',
        };
        _replaceBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("Label.Replace", "Replace"),
            Text = string.Empty,
            IsEnabled = true,
            HotKeyChar = 'E',
        };
        _previewList = new ListBox<string>(new ListBoxModel<string>(_previewRows), themeService)
        {
            EmptyText = UiText.Get("Empty.MultiRename", " Mark files, or leave the cursor on one, then type a pattern."),
        };
        // A preview is read, not chosen: a focusable list would take Enter ahead of the Rename command.
        _previewList.CanFocus = false;
        _countLabel = new Label(string.Empty, themeService);
        _errorLabel = new Label(string.Empty, themeService);
        _renameButton = new Button(UiText.Get("Menu.Rename", "Rename"), _renameCommand, themeService)
        {
            HotKeyChar = iConsole.Localization.HotKeys.VisibleIn(UiText.Get("Menu.Rename", "Rename"), 'R'),
        };
        _cancelButton = new CancelDialogButton(themeService);

        AddControl(_patternBox);
        AddControl(_findBox);
        AddControl(_replaceBox);
        AddControl(_countLabel);
        AddControl(_previewList);
        AddControl(_errorLabel);
        AddControl(_renameButton);
        AddControl(_cancelButton);
    }

    /// <summary>The panel's marked entries (or the cursor's), as the batch will see them.</summary>
    public IContentModel? SourceModel { get; private set; }

    /// <summary>The source URLs, in batch order; <see cref="RenameRow.Index"/> indexes this list.</summary>
    public IReadOnlyList<Url> SourceUrls { get; private set; } = [];

    /// <summary>The folder every new name is created in: the source panel's folder.</summary>
    public Url? FolderUrl => SourceModel?.Url;

    /// <summary>One row per source, in batch order, as the planner decided.</summary>
    public IReadOnlyList<RenameRow> Plan { get; private set; } = [];

    /// <summary>How many rows will be renamed. Counted once per plan, not on every read.</summary>
    public int OkCount { get; private set; }

    public Label ErrorMessage => _errorLabel;

    public string PatternText => _patternBox.Text ?? string.Empty;

    public string FindText => _findBox.Text ?? string.Empty;

    public string ReplaceText => _replaceBox.Text ?? string.Empty;

    protected override void ClearError() => _errorLabel.Text = string.Empty;

    /// <summary>Takes the batch from the panel. Called by <see cref="MultiRenameDialogCommand"/> before the dialog is shown.</summary>
    public void Prepare(IContentModel model, IReadOnlyList<Url> urls)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(urls);

        SourceModel = model;
        SourceUrls = urls;
        _folderNames = [.. model.Entries
            .Where(entry => !FocusedEntry.IsParentEntry(entry) && entry is not Model.ContentModels.IDriveObjectEntry)
            .Select(entry => FocusedEntry.NameOf(entry.Url))];

        _patternBox.Text = MultiRenamePattern.DefaultPattern;
        _findBox.Text = string.Empty;
        _replaceBox.Text = string.Empty;
        _errorLabel.Text = string.Empty;
        _computedFor = null;
        RefreshPlanIfChanged();
    }

    public void Reset()
    {
        SourceModel = null;
        SourceUrls = [];
        Plan = [];
        OkCount = 0;
        _folderNames = [];
        _previewRows.ReplaceAll([]);
        _countLabel.Text = string.Empty;
        _errorLabel.Text = string.Empty;
        _computedFor = null;
    }

    /// <summary>Recomputes the plan when a box has changed since the last time, so typing is reflected at once.</summary>
    public void RefreshPlanIfChanged()
    {
        var current = (PatternText, FindText, ReplaceText);
        if (_computedFor is { } computed && computed == current)
        {
            return;
        }

        _computedFor = current;
        Recompute();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("DialogHint.MultiRename", "Rename the files shown in the preview"));
        yield return ("Esc", UiText.Get("DialogHint.CloseWithoutChanging", "Close without changing anything"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool handled = await base.HandleKeyEventAsync(keyEvent);
        RefreshPlanIfChanged();
        return handled;
    }

    private void Recompute()
    {
        var entries = new List<(string OldName, string NewName)>(SourceUrls.Count);
        for (var index = 0; index < SourceUrls.Count; index++)
        {
            var url = SourceUrls[index];
            var oldName = FocusedEntry.NameOf(url);
            var newName = MultiRenamePattern.Expand(PatternText, oldName, url.PathType == PathType.Folder, index + 1, FindText, ReplaceText);
            entries.Add((oldName, newName));
        }

        Plan = MultiRenamePlanner.Plan(entries, _folderNames, OperatingSystem.IsWindows());
        OkCount = Plan.Count(row => row.Status == RenameStatus.Ok);
        _previewRows.ReplaceAll([.. Plan.Take(PreviewLimit).Select(FormatRow)]);

        var ok = OkCount;
        _countLabel.Text = string.Format(UiText.Get("Multi.Count", "{0} to rename, {1} not renamed"), ok, Plan.Count - ok);
    }

    private static string FormatRow(RenameRow row)
    {
        var line = string.Format(UiText.Get("Multi.Row", "{0} -> {1}"), row.OldName, row.NewName);
        var status = StatusText(row.Status);
        return status.Length == 0 ? line : line + "  " + status;
    }

    private static string StatusText(RenameStatus status) => status switch
    {
        RenameStatus.Unchanged => UiText.Get("Multi.Status.Unchanged", "unchanged"),
        RenameStatus.CaseOnly => UiText.Get("Multi.Status.CaseOnly", "change of case only, not renamed"),
        RenameStatus.Empty => UiText.Get("Multi.Status.Empty", "empty"),
        RenameStatus.InvalidName => UiText.Get("Multi.Status.InvalidName", "invalid name"),
        RenameStatus.Blocked => UiText.Get("Multi.Status.Blocked", "blocked by another entry"),
        RenameStatus.Duplicate => UiText.Get("Multi.Status.Duplicate", "duplicate"),
        RenameStatus.Exists => UiText.Get("Multi.Status.Exists", "already exists"),
        _ => string.Empty,
    };

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(new Size(
                Math.Clamp((int)Parent.ViewSize.Width - 8, 60, 110),
                Math.Clamp((int)Parent.ViewSize.Height - 4, 18, 34)));
        }

        var height = (int)Math.Max(1, ViewSize.Height);
        _patternBox.Position = new Position(FieldX, 1);
        _patternBox.ViewSize = new Size(FieldWidth, 1);
        _patternBox.ScrollableModel.ViewSize = _patternBox.ViewSize;
        _findBox.Position = new Position(FieldX, 2);
        _findBox.ViewSize = new Size(FieldWidth, 1);
        _findBox.ScrollableModel.ViewSize = _findBox.ViewSize;
        _replaceBox.Position = new Position(FieldX, 3);
        _replaceBox.ViewSize = new Size(FieldWidth, 1);
        _replaceBox.ScrollableModel.ViewSize = _replaceBox.ViewSize;
        _countLabel.Position = new Position(FieldX, 4);
        _countLabel.ViewSize = new Size(FieldWidth, 1);
        _previewList.Position = new Position(FieldX, 5);
        _previewList.ViewSize = new Size(FieldWidth, Math.Max(1, height - 9));
        _errorLabel.Position = new Position(FieldX, Math.Max(0, height - 3));
        _errorLabel.ViewSize = new Size(FieldWidth, 1);
        LayoutButtonRow(_renameButton, _cancelButton);
    }
}
