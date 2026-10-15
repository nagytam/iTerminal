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
    private ListBox<IBackgroundTask> _listBox { get; set; }
    private ScrollBar _scrollBar { get; set; }

    public TaskManagerDialog(TaskManager taskManager, IThemeService themeService) 
        : base(nameof(TaskManagerDialog), themeService)
    {
        _taskManager = taskManager ?? new TaskManager();
        Title = "Task Manager";
        var listModel = new ListBoxModel<IBackgroundTask>(_taskManager.Tasks)
        {
            TransformItem = TransformItem
        };
        _listBox = new ListBox<IBackgroundTask>(listModel, themeService)
        {
            // An empty frame read as "the list failed to load"; most of the time nothing is running.
            EmptyText = " No background tasks are running.",
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
            Value = item.DisplayName.Shrink((int)_listBox.ViewSize.Width - 2) ?? string.Empty
        };
        return transformedItem;
    }

    public override void OnIdle()
    {
        Title = $"Task Manager ({_listBox.Count})";
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
