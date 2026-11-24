using iTerminal.Core.Services.BackgroundStart;
using iConsole.Controls;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Data;
using iTerminal.Core.Model;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The network interfaces as a table bound to <see cref="NetworkInfoModel.Networks"/>. The rows are read from the live
/// <see cref="NetworkInfo"/> on every draw, so a status that changes (<see cref="Jobs.NetworkInfoJob"/> re-reads the
/// interfaces every ten seconds and replaces a changed one) shows on the next repaint, rather than the text formatted
/// once when the row arrived.
/// </summary>
public class NetworkManagerDialog : Dialog
{
    internal const int NameColumnWidth = 30;
    internal const int StatusColumnWidth = 12;
    private const int MinimumNameWidth = 8;
    private const int MinimumDescriptionWidth = 4;

    private readonly NetworkInfoModel _networkInfoModel;
    private readonly Table<NetworkInfo> _table;
    private readonly ScrollBar _scrollBar;
    private readonly IBackgroundStartService? _backgroundStart;

    private const string ReadingText = " Reading the network interfaces…";
    private const string NoneText = " No network interfaces found.";

    public NetworkManagerDialog(TaskManager taskManager, NetworkInfoModel networkInfoModel, IThemeService themeService, IBackgroundStartService? backgroundStart = null)
        : base(nameof(NetworkManagerDialog), themeService)
    {
        _backgroundStart = backgroundStart;
        _networkInfoModel = networkInfoModel ?? new NetworkInfoModel();

        Title = UiText.Get("Dialog.NetworkManager", "Network manager");
        _table = new Table<NetworkInfo>(_networkInfoModel.Networks, themeService)
        {
            EmptyText = ReadingText,

            // An interface that is not up is greyed, as its row was before it became a column.
            RowForeground = network => network.NetworkInterface is { } nic && nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                ? ThemeService.CurrentColorScheme.DisabledForeground
                : null,
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollBarOrientation = Orientation.Vertical,
            ScrollableModel = _table.ScrollableModel
        };
        AddControl(_table);
        AddControl(_scrollBar);
    }

    /// <summary>The interface under the cursor, for a test or a later command.</summary>
    internal NetworkInfo? SelectedNetwork => _table.SelectedRow;

    internal Table<NetworkInfo> TableForTest => _table;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
    }

    public override void OnIdle()
    {
        _table.EmptyText = _networkInfoModel.IsLoaded ? NoneText : ReadingText;
        base.OnIdle();
    }

    public override async Task ShowAsync()
    {
        // The interfaces are read when the manager is opened, or as configured (BackgroundStart.NetworkInfo).
        _backgroundStart?.Request(BackgroundServices.NetworkInfo);
        await base.ShowAsync();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Parent != null)
        {
            CenterInParent(Parent.ViewSize * 0.9);
        }

        _table.Position = new Position(1, 1);
        _table.ViewSize = new Size(ViewSize.Width - 2, ViewSize.Height - 2);
        // The table's own layout gives the body one row less than the header; the scroll bar follows the body.
        _table.ScrollableModel.ViewSize = new Size(_table.ViewSize.Width, Math.Max(1, _table.ViewSize.Height - 1));
        ConfigureColumns();

        _scrollBar.Position = new Position(ViewSize.Width - 1, 1);
        _scrollBar.ViewSize = new Size(1, ViewSize.Height - 2);
    }

    /// <summary>
    /// The name and the description share the width that is left: the name is capped at <see cref="NameColumnWidth"/>, the
    /// status keeps its column, and the description takes the rest. Each column is cut to its width, so a long filter-driver
    /// name cannot push the status into the next column.
    /// </summary>
    private void ConfigureColumns()
    {
        int width = (int)_table.ViewSize.Width;
        int nameWidth = Math.Clamp(width / 3, MinimumNameWidth, NameColumnWidth);
        int descriptionWidth = Math.Max(MinimumDescriptionWidth, width - nameWidth - StatusColumnWidth - 2);

        _table.Columns.Clear();
        _table.Columns.Add(new ColumnDefinition<NetworkInfo>(UiText.Get("Network.Column.Name", "Name"), nameWidth, n => n.Name.Trim()));
        _table.Columns.Add(new ColumnDefinition<NetworkInfo>(UiText.Get("Network.Column.Status", "Status"), StatusColumnWidth,
            n => n.NetworkInterface?.OperationalStatus.ToString() ?? string.Empty));
        _table.Columns.Add(new ColumnDefinition<NetworkInfo>(UiText.Get("Network.Column.Description", "Description"), descriptionWidth,
            n => n.NetworkInterface?.Description ?? string.Empty));
    }
}
