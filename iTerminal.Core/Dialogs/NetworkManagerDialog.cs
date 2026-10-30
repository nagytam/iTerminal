using iConsole.Controls;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Data;
using iTerminal.Core.Model;

namespace iTerminal.Core.Dialogs;

public class NetworkManagerDialog : Dialog
{
    private TaskManager _taskManager { get; set; }
    private NetworkInfoModel _networkInfoModel { get; set; }
    private ListBox<NetworkInfo> _listBox { get; set; }
    private ScrollBar _scrollBar { get; set; }

    public NetworkManagerDialog(TaskManager taskManager, NetworkInfoModel networkInfoModel, IThemeService themeService) 
        : base(nameof(NetworkManagerDialog), themeService)
    {
        _taskManager = taskManager ?? new TaskManager();
        _networkInfoModel = networkInfoModel ?? new NetworkInfoModel();

        Title = iConsole.Localization.UiText.Get("Dialog.NetworkManager", "Network manager");
        var listModel = new ListBoxModel<NetworkInfo>(_networkInfoModel.Networks)
        {
            TransformItem = TransformItem
        };
        _listBox = new ListBox<NetworkInfo>(listModel, themeService)
        {
            EmptyText = ReadingText,
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollBarOrientation = Orientation.Vertical,
            ScrollableModel = _listBox.ScrollableModel
        };
        AddControl(_listBox);
        AddControl(_scrollBar);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
    }

    private const string ReadingText = " Reading the network interfaces…";
    private const string NoneText = " No network interfaces found.";

    public override void OnIdle()
    {
        _listBox.EmptyText = _networkInfoModel.IsLoaded ? NoneText : ReadingText;
        base.OnIdle();
    }

    internal const int NameColumnWidth = 30;
    internal const int StatusColumnWidth = 12;

    /// <summary>
    /// One interface as a row of fixed columns. A name longer than its column is shortened in the
    /// middle rather than allowed to push the rest along: Windows lists filter drivers as
    /// "Local Area Connection* 8-WFP Native MAC Layer LightWeight Filter-0000", and those rows ran
    /// their status straight into the name ("…Filter-0000Up").
    /// </summary>
    internal static string FormatRow(string? name, string status, string? description)
    {
        var shownName = (name ?? string.Empty).Trim();
        if (shownName.Length > NameColumnWidth - 1)
        {
            shownName = shownName.Shrink(NameColumnWidth - 1) ?? string.Empty;
        }
        return shownName.PadRight(NameColumnWidth) + status.PadRight(StatusColumnWidth) + " " + description;
    }
    private ListItem<NetworkInfo> TransformItem(NetworkInfo item, CancellationToken cancellationToken)
    {
        ListItem<NetworkInfo> transformedItem = new ListItem<NetworkInfo>(item);
        transformedItem.Value = FormatRow(item.Name, item.NetworkInterface.OperationalStatus.ToString(), item.NetworkInterface.Description);
        transformedItem.IsEnabled = item.NetworkInterface.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up;
        return transformedItem;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.9);
        }

        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 2);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(ViewSize.Width - 1, 1);
        _scrollBar.ViewSize = new Size(1, ViewSize.Height - 2);
    }
}
