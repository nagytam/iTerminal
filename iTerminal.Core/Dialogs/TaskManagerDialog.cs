using iConsole.Localization;
using iConsole.Controls;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;

namespace iTerminal.Core.Dialogs;

public class TaskManagerDialog : Dialog
{
    private readonly TaskManager _taskManager;
    private readonly ListBoxModel<IBackgroundTask> _listModel;
    private ListBox<IBackgroundTask> _listBox { get; set; }
    private ScrollBar _scrollBar { get; set; }

    // The task the Delete key has asked about. It is the one the question named, even if the list has moved on since.
    private IBackgroundTask? _taskToCancel;

    public TaskManagerDialog(TaskManager taskManager, IThemeService themeService) 
        : base(nameof(TaskManagerDialog), themeService)
    {
        _taskManager = taskManager ?? new TaskManager();
        Title = iConsole.Localization.UiText.Get("Menu.TaskManager", "Task Manager");
        _listModel = new ListBoxModel<IBackgroundTask>(_taskManager.Tasks)
        {
            TransformItem = TransformItem
        };
        _listBox = new ListBox<IBackgroundTask>(_listModel, themeService)
        {
            // An empty frame read as "the list failed to load"; most of the time nothing is running.
            EmptyText = UiText.Get("Empty.Tasks", " No background tasks are running."),
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel
        };

        AddControl(_listBox);
        AddControl(_scrollBar);
    }

    private ListItem<IBackgroundTask> TransformItem(IBackgroundTask item, CancellationToken cancellationToken)
    {
        var transformedItem = new ListItem<IBackgroundTask>(item)
        {
            Value = (item.PauseGate.IsPaused ? "[paused] " : string.Empty) + item.DisplayName.Shrink((int)_listBox.ViewSize.Width - 4) ?? string.Empty
        };
        return transformedItem;
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down && _taskToCancel is { } asked)
        {
            // Cancelling a task throws away what it has done so far, so the question is answered first: y or Delete
            // cancels it, and any other key keeps the task running.
            _taskToCancel = null;
            bool plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
            if (plain && (keyEvent.ConsoleKey == ConsoleKey.Delete || char.ToLowerInvariant(keyEvent.KeyChar) == 'y'))
            {
                asked.CancellationTokenSource.Cancel();
            }

            StateHasChanged();
            return true;
        }

        if (keyEvent.KeyPressType == KeyPressType.Down && CursorTask() is { } task)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.P && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
            {
                _taskManager.SetPaused(task, !task.PauseGate.IsPaused);
                _listModel.Invalidate();
                return true;
            }

            if (keyEvent.Control && keyEvent.ConsoleKey is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
            {
                // Moves the queued task; once it has started it is no longer in the queue, and the key does nothing.
                _taskManager.MoveInQueue(task, earlier: keyEvent.ConsoleKey == ConsoleKey.UpArrow);
                _listModel.Invalidate();
                return true;
            }

            if (keyEvent.ConsoleKey == ConsoleKey.Delete && !keyEvent.Control && !keyEvent.Alt)
            {
                _taskToCancel = task;
                StateHasChanged();
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>The task on the cursor row. Enter marks a row as selected, but a cursor move alone does not, so the keys act on this.</summary>
    private IBackgroundTask? CursorTask()
    {
        var items = _listBox.ListModel.ListItems;
        int cursor = (int)_listBox.ScrollableModel.CursorPosition.Y;
        return cursor >= 0 && cursor < items.Count ? items[cursor].Item : null;
    }

    /// <summary>The keys this dialog adds to the list's.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_taskToCancel is not null)
        {
            yield return ("y / Del", UiText.Get("DialogHint.CancelTask", "Cancel the task"));
            yield return ("Any other key", UiText.Get("DialogHint.KeepTaskRunning", "Keep the task running"));
            yield break;
        }

        // With no task under the cursor these keys do nothing, so the footer does not name them.
        if (CursorTask() is null)
        {
            yield break;
        }

        yield return ("P", UiText.Get("DialogHint.PauseResumeTask", "Pause or resume the task"));
        yield return ("Ctrl+↑/↓", UiText.Get("DialogHint.MoveQueuedTask", "Move a queued task"));
        yield return ("Del", UiText.Get("DialogHint.CancelTask", "Cancel the task"));
    }

    public override void OnIdle()
    {
        Title = _taskToCancel is { } asked
            ? string.Format(UiText.Get("Dialog.TaskManagerCancelQuestion", "Cancel \"{0}\"?"), asked.DisplayName)
            : string.Format(UiText.Get("Dialog.TaskManagerTitle", "Task Manager ({0})"), _listBox.Count);
        base.OnIdle();
    }

    public override void OnInitializeLayout()
    {
        // Sized first, so the list and scroll bar below are laid out against this size rather than
        // the one the dialog had before (nothing, the first time it opens).
        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        base.OnInitializeLayout();

        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 2);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(ViewSize.Width - 1, 1);
        _scrollBar.ViewSize = new Size(1, ViewSize.Height - 2);
    }
}
