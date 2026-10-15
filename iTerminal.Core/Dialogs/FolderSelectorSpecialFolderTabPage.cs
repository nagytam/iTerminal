using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls;
using iConsole.Controls.ScrollBar;
using iConsole.Services;
using iTerminal.Core.Commands;
using iConsole.Data;
using System.Collections.Specialized;
using iConsole.Model;
using iConsole.Helper;
using iConsole.Interfaces;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// The places a panel can be pointed at that are not a drive letter and not somewhere the user has
/// already been: the operating system's own folders, and the drives that exist only inside this app
/// (the memory drive, the music catalog, the playing queue).
///
/// The virtual drives are listed here rather than on the Drives page because that page is fed by
/// <see cref="iTerminal.Core.Model.DriveInfoModel"/>, which the status bar's drive selector also
/// draws from — a music drive added there would appear in a list that is meant to be the machine's
/// real volumes. They are listed *somewhere*, rather than left to the Path page, because a URL
/// scheme nobody has written down cannot be typed.
/// </summary>
public class FolderSelectorSpecialFoldersTabPage : FolderSelectorTabPage
{
    private ListBox<string> _listBox { get; set; }
    private ScrollBar _scrollBar { get; set; }
    private Button _selectSpecialFolderButton { get; set; }
    public ScrollableModel ScrollableModel => _listBox.ScrollableModel;
    private ConcurrentObservableCollection<string> _specialFolders { get; set; } = [];

    /// <summary>The listed places, in list order.</summary>
    public IReadOnlyList<string> Places => [.. _specialFolders.AsSnapshot()];

    private ICommand _selectSpecialFolderCommand { get; set; }
    protected override ICommand? SelectCommand => _selectSpecialFolderCommand;
    protected override string SelectDescription => "Go to the place under the cursor.";

    /// <summary>
    /// The app's own drives, in the order they are listed. Kept as URLs rather than as
    /// <see cref="Container"/> constants at the call site so a scheme added to the app has one
    /// obvious place to be added to the selector.
    /// </summary>
    private static readonly string[] VirtualDrives =
    [
        Container.MEMORY_DRIVE,
        Container.MUSIC_CATALOG,
        Container.MUSIC_PLAYLIST,
        Container.DOCKER,
        Container.GIT,
        Container.SEARCH_RESULTS,
        Container.RECYCLE,
    ];

    public FolderSelectorSpecialFoldersTabPage(
        IContentModelService contentModelService,
        IThemeService themeService)
        : base("3-Places", ConsoleKey.D3, themeService)
    {
        _specialFolders.CollectionChanged += OnCollectionChanged;
        var listModel = new ListBoxModel<string>(_specialFolders)
        {
            TransformItem = TransformItem
        };
        _listBox = new ListBox<string>(listModel, themeService);
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel
        };
        _selectSpecialFolderCommand = new SelectSpecialFolderCommand(contentModelService, _specialFolders);
        _selectSpecialFolderButton = new Button("Select folder", _selectSpecialFolderCommand, themeService)
        {
            HotKeyChar = 'S'
        };
        AddControl(_listBox);
        AddControl(_scrollBar);
        AddControl(_selectSpecialFolderButton);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        _specialFolders.Clear();
        foreach (var specialFolder in GetSpecialFolders())
        {
            Add(specialFolder);
        }
        foreach (var virtualDrive in VirtualDrives)
        {
            Add(virtualDrive);
        }
    }

    /// <summary>
    /// Each folder is resolved on its own. <see cref="Environment.GetFolderPath"/> answers with an
    /// empty string for a folder the platform does not have — most of the Common* ones on Linux —
    /// and one of those in the middle of the list used to take every folder after it down with it,
    /// because the whole block was wrapped in a single try.
    /// </summary>
    private static IEnumerable<string> GetSpecialFolders()
    {
        Environment.SpecialFolder[] specialFolders =
        [
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.CommonDesktopDirectory,
            Environment.SpecialFolder.CommonPictures,
            Environment.SpecialFolder.CommonVideos,
            Environment.SpecialFolder.Recent,
        ];

        foreach (var specialFolder in specialFolders)
        {
            string? path = null;
            try
            {
                path = Environment.GetFolderPath(specialFolder);
            }
            catch
            {
                // A folder this platform cannot resolve is simply not a place to offer.
            }
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }

        string? tempPath = null;
        try
        {
            tempPath = System.IO.Path.GetTempPath();
        }
        catch
        {
        }
        if (!string.IsNullOrWhiteSpace(tempPath))
        {
            yield return tempPath;
        }
    }

    /// <summary>
    /// Adds a place unless it is already listed. Several special folders resolve to the same path on
    /// a stock install (MyDocuments and UserProfile on some Linux setups), and a list that offers
    /// the same row twice reads as a bug.
    /// </summary>
    private void Add(string place)
    {
        if (!_specialFolders.Any(f => string.Equals(f, place, StringComparison.OrdinalIgnoreCase)))
        {
            _specialFolders.Add(place);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollableModel.ScrollableSize = new Size(1, (uint)_specialFolders.Count);
        StateHasChanged();
    }

    private ListItem<string> TransformItem(string item, CancellationToken cancellationToken)
    {
        var transformedItem = new ListItem<string>(item)
        {
            Value = item.Shrink((int)_listBox.ViewSize.Width - 2),
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

        _selectSpecialFolderButton.Position = new Position(1, Math.Max(0, ViewSize.Height - 1));
        _selectSpecialFolderButton.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        base.OnInitializeLayout();
    }
}
