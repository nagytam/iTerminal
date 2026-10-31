using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls.Experimental;
using iConsole.Services;
using iConsole.Data;
using iTerminal.Core.Model;
using System.ComponentModel;
using iConsole.Buffers;
using iTerminal.Core.Commands;
using iConsole.Controls;
using iConsole.Model;
using iConsole.Interfaces;

namespace iTerminal.Core.Dialogs;

public class FolderSelectorFolderTreeTabPage : FolderSelectorTabPage
{
    private DynamicLabel<IHierarchicalItemModel> _dynamicLabel { get; set; }
    private TreeView _treeView { get; set; }
    private Button _selectFolderButton { get; set; }
    private IFrequentExecutionGuardService _frequentExecutionGuardService { get; set; }

    private ICommand _selectFolderCommand { get; set; }
    protected override ICommand? SelectCommand => _selectFolderCommand;
    protected override string SelectDescription => iConsole.Localization.UiText.Get("FolderSelector.Select.Tree", "Go to the folder under the cursor in the tree.");

    public FolderSelectorFolderTreeTabPage(
        IContentModelService contentModelService,
        FolderIndexModel folderIndexModel,
        IFrequentExecutionGuardService frequentExecutionGuardService,
        IThemeService themeService)
        : base("4-Folder Tree", ConsoleKey.D4, themeService)
    {
        folderIndexModel ??= new FolderIndexModel();
        _frequentExecutionGuardService = frequentExecutionGuardService;
        _treeView = new TreeView(folderIndexModel, themeService);

        var observableValue = new ObservableValue<IHierarchicalItemModel>(
            folderIndexModel,
            (m) => $"Number of folders: {m.Count:N0}");
        _dynamicLabel = new DynamicLabel<IHierarchicalItemModel>(observableValue, themeService);

        _selectFolderCommand = new SelectTreeFolderCommand(contentModelService, folderIndexModel);
        _selectFolderButton = new Button(iConsole.Localization.UiText.Get("Button.SelectFolder", "Select folder"), _selectFolderCommand, themeService)
        {
            HotKeyChar = 'S'
        };
        AddControl(_treeView);
        AddControl(_dynamicLabel);
        AddControl(_selectFolderButton);

        folderIndexModel.PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _frequentExecutionGuardService.Execute(
            () =>
            {
                OnInitializeLayout();
                StateHasChanged();
            });
    }

    public override void OnInitializeLayout()
    {
        _treeView.Position = new Position(1, 0);
        _treeView.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), Math.Max(0, ViewSize.Height - 4));

        _dynamicLabel.Position = new Position(1, Math.Max(0, ViewSize.Height - 2));
        _dynamicLabel.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        _selectFolderButton.Position = new Position(1, Math.Max(0, ViewSize.Height - 1));
        _selectFolderButton.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        base.OnInitializeLayout();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
    }
}

