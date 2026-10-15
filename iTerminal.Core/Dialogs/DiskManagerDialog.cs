using iConsole.Controls;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Data;
using iTerminal.Core.Model;

namespace iTerminal.Core.Dialogs;

public class DiskManagerDialog : Dialog
{
    private TaskManager _taskManager { get; set; }
    private DriveInfoModel _driveInfoModel { get; set; }
    private ListBox<Drive> _listBox { get; set; }
    private ScrollBar _scrollBar { get; set; }

    public DiskManagerDialog(TaskManager taskManager, DriveInfoModel driveInfoModel, IThemeService themeService) 
        : base(nameof(DiskManagerDialog), themeService)
    {
        // Support reflection-based instantiation in unit tests where dependencies
        // may be provided as null. Use simple defaults to avoid NullReferenceException
        // during construction while preserving normal DI behavior when non-null
        // instances are supplied.
        _taskManager = taskManager ?? new TaskManager();
        _driveInfoModel = driveInfoModel ?? new DriveInfoModel();
        Title = "Disk manager";
        var listModel = new ListBoxModel<Drive>(_driveInfoModel.Drives)
        {
            TransformItem = TransformItem
        };
        _listBox = new ListBox<Drive>(listModel, themeService)
        {
            EmptyText = ReadingText,
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel
        };

        AddControl(_listBox);
        AddControl(_scrollBar);
    }

    private const string ReadingText = " Reading the drives…";
    private const string NoneText = " No drives found.";

    public override void OnIdle()
    {
        _listBox.EmptyText = _driveInfoModel.IsLoaded ? NoneText : ReadingText;
        base.OnIdle();
    }

    private ListItem<Drive> TransformItem(Drive item, CancellationToken cancellationToken)
    {
        var transformedItem = new ListItem<Drive>(item)
        {
            Value = item.Name,
            IsEnabled = item.IsEnabled
        };
        return transformedItem;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.8);
        }

        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 2);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(ViewSize.Width - 1, 1);
        _scrollBar.ViewSize = new Size(1, ViewSize.Height - 2);
    }
}
