using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls;
using iConsole.Controls.ScrollBar;
using iConsole.Services;
using iTerminal.Core.Commands;
using iConsole.Data;
using System.Collections.Specialized;
using iTerminal.Core.Services;
using iConsole.Model;
using iConsole.Helper;
using iConsole.Interfaces;

namespace iTerminal.Core.Dialogs;

public class FolderSelectorHistoryTabPage : FolderSelectorTabPage
{
    private ListBox<string> _listBox { get; set; }
    private ScrollBar _scrollBar { get; set; }
    private Button _selectFolderButton { get; set; }
    public ScrollableModel ScrollableModel => _listBox.ScrollableModel;

    private ICommand _selectFolderCommand { get; set; }
    protected override ICommand? SelectCommand => _selectFolderCommand;
    protected override string SelectDescription => "Go back to the folder under the cursor.";

    public FolderSelectorHistoryTabPage(
        FolderHistoryService folderHistoryService,
        IContentModelService contentModelService,
        IThemeService themeService)
        : base("2-Recent", ConsoleKey.D2, themeService)
    {
        folderHistoryService ??= new FolderHistoryService(NullSettingsService.Instance);

        folderHistoryService.History.CollectionChanged += OnCollectionChanged;
        var listModel = new ListBoxModel<string>(folderHistoryService.History)
        {
            FilterAndSortItems = FilterAndSortItems,
            TransformItem = TransformItem
        };
        _listBox = new ListBox<string>(listModel, themeService)
        {
            EmptyText = " No folders visited yet.",
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel
        };
        _selectFolderCommand = new SelectHistoryFolderCommand(contentModelService, folderHistoryService);
        _selectFolderButton = new Button("Select folder", _selectFolderCommand, themeService)
        {
            HotKeyChar = 'S'
        };
        AddControl(_listBox);
        AddControl(_scrollBar);
        AddControl(_selectFolderButton);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        StateHasChanged();
    }

    private ListItem<string> TransformItem(string item, CancellationToken cancellationToken)
    {
        var transformedItem = new ListItem<string>(item)
        {
            Value = item.Shrink((int)_listBox.ViewSize.Width - 2)!,
            IsEnabled = true
        };
        return transformedItem;
    }

    public override void OnInitializeLayout()
    {
        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), Math.Max(0, ViewSize.Height - 3));
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(Math.Max(0, ViewSize.Width - 1), 1);
        _scrollBar.ViewSize = new Size(1, Math.Max(0, ViewSize.Height - 3));

        _selectFolderButton.Position = new Position(1, Math.Max(0, ViewSize.Height - 1));
        _selectFolderButton.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        base.OnInitializeLayout();
    }

    private IEnumerable<string> FilterAndSortItems(IEnumerable<string> items, CancellationToken cancellationToken)
    {
        return items;
    }
}

