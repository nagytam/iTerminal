using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls;
using iConsole.Controls.ScrollBar;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Commands;
using iTerminal.Core.Model;
using iConsole.Data;
using System.Collections.Specialized;
using iConsole.Interfaces;
using iConsole.Model;

namespace iTerminal.Core.Dialogs;

public class FolderSelectorDrivesTabPage : FolderSelectorTabPage
{
    private ListBox<Drive> _listBox { get; set; }
    private ScrollBar _scrollBar { get; set; }
    private Button _selectDriveButton { get; set; }
    private DriveInfoModel _driveInfoModel { get; set; }
    private IContentModelService _contentModelService { get; set; }
    public ScrollableModel ScrollableModel => _listBox.ScrollableModel;

    /// <summary>
    /// The drive under the cursor. <see cref="ListBox{T}.SelectedItem"/> reports the row whose
    /// <c>IsSelected</c> flag is set — a flag only Enter ever sets — so on a freshly opened page it
    /// is null and the cursor row is the only thing that means anything.
    /// </summary>
    public Drive? SelectedDrive
    {
        get
        {
            var listItems = _listBox.ListModel.ListItems;
            var cursorIndex = (int)_listBox.ScrollableModel.CursorPosition.Y;
            if (cursorIndex >= 0 && cursorIndex < listItems.Count)
            {
                return listItems[cursorIndex].Item;
            }
            return _listBox.SelectedItem?.Item;
        }
    }

    private ICommand _selectDriveCommand { get; set; }
    protected override ICommand? SelectCommand => _selectDriveCommand;
    protected override string SelectDescription => iConsole.Localization.UiText.Get("FolderSelector.Select.Drive", "Go to the drive under the cursor.");

    public FolderSelectorDrivesTabPage(
        DriveInfoModel driveInfoModel,
        IContentModelService contentModelService,
        IThemeService themeService)
        : base("1-Drives", ConsoleKey.D1, themeService)
    {
        _driveInfoModel = driveInfoModel ?? new DriveInfoModel();
        _driveInfoModel.Drives.CollectionChanged += OnCollectionChanged;
        _contentModelService = contentModelService;

        var listModel = new ListBoxModel<Drive>(_driveInfoModel.Drives)
        {
            FilterAndSortItems = FilterAndSortItems,
            TransformItem = TransformItem,
            IsItemEnabled = IsEnabledItem
        };
        _listBox = new ListBox<Drive>(listModel, themeService);
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel
        };
        _selectDriveCommand = new SelectDriveCommand(_contentModelService);
        _selectDriveButton = new Button(iConsole.Localization.UiText.Get("Button.SelectDrive", "Select drive"), _selectDriveCommand, themeService)
        {
            HotKeyChar = 'S'
        };

        AddControl(_listBox);
        AddControl(_scrollBar);
        AddControl(_selectDriveButton);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), Math.Max(0, ViewSize.Height - 3));
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(Math.Max(0, ViewSize.Width - 1), 1);
        _scrollBar.ViewSize = new Size(1, Math.Max(0, ViewSize.Height - 3));

        _selectDriveButton.Position = new Position(1, Math.Max(0, ViewSize.Height - 1));
        _selectDriveButton.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        base.OnInitializeLayout();
    }

    private IEnumerable<Drive> FilterAndSortItems(IEnumerable<Drive> items, CancellationToken cancellationToken)
    {
        return items.OrderBy(d => !d.IsEnabled).ThenBy(d => d.Name);
    }

    private ListItem<Drive> TransformItem(Drive item, CancellationToken cancellationToken)
    {
        var transformedItem = new ListItem<Drive>(item) {
            Value = item.Name,
            IsEnabled = item.IsEnabled
        };
        return transformedItem;
    }

    private bool IsEnabledItem(Drive item, CancellationToken cancellationToken)
    {
        return item.IsEnabled;
    }
}

