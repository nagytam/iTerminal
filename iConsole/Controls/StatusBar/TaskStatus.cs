using iConsole.Data;
using iConsole.Services;
using iConsole.Tasks;

namespace iConsole.Controls.StatusBar;

public class TaskStatus : StatusBarItem
{
    private TaskManager _taskManager { get; set; }

    public TaskStatus(TaskManager taskManager, IThemeService themeService) : base(themeService)
    {
        _taskManager = taskManager; 
        ViewSize = new Size(10, 1);
    }

    public override void OnIdle()
    {
        var text = $"Tasks: {_taskManager.Tasks.Count}".PadRight((int)ViewSize.Width);
        if (text != Text)
        {
            Text = text;
            StateHasChanged();
        }
        base.OnIdle();
    }
}
