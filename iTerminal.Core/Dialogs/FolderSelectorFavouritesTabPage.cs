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

public class FolderSelectorFavouritesTabPage : FolderSelectorTabPage
{
    private string _key = "Favourites";
    private ISettingsService _settingsService { get; set; }
    private ConcurrentObservableCollection<Url> _favourites { get; set; }
    private ListBox<Url> _listBox { get; set; }
    private ScrollBar _scrollBar { get; set; }
    private Button _addToFavouritesButton { get; set; }
    private Button _deleteFavouriteButton { get; set; }
    private Button _selectFavouriteButton { get; set; }
    public ScrollableModel ScrollableModel => _listBox.ScrollableModel;

    /// <summary>The favourites as they are listed, in list order. Exposed for the select command
    /// and for tests, which otherwise could not tell an empty list from an unread one.</summary>
    public IReadOnlyList<Url> Favourites => [.. _favourites.AsSnapshot()];

    private ICommand _selectFavouriteCommand { get; set; }
    protected override ICommand? SelectCommand => _selectFavouriteCommand;
    protected override string SelectDescription => "Go to the favourite folder under the cursor.";

    public FolderSelectorFavouritesTabPage(
        IContentModelService contentModelService,
        ISettingsService settingsService,
        IThemeService themeService)
        : base("0-Favourites", ConsoleKey.D0, themeService)
    {
        _settingsService = settingsService ?? NullSettingsService.Instance;
        _favourites = [];

        _favourites.CollectionChanged += OnCollectionChanged;
        var listModel = new ListBoxModel<Url>(_favourites)
        {
            FilterAndSortItems = FilterAndSortItems,
            TransformItem = TransformItem
        };
        _listBox = new ListBox<Url>(listModel, themeService)
        {
            // A first run has no favourites, and a blank page did not say how to get one.
            EmptyText = " No favourites yet — Add to favourites saves the folder the panel is showing.",
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel
        };
        _selectFavouriteCommand = new SelectFavouriteCommand(
            contentModelService,
            _listBox.ScrollableModel,
            _favourites);
        _selectFavouriteButton = new Button("Select favourite", _selectFavouriteCommand, themeService)
        {
            HotKeyChar = 'S'
        };
        _addToFavouritesButton = new Button("Add to favourites",
            new AddToFavouritesCommand(
                contentModelService,
                _favourites), themeService)
        {
            HotKeyChar = 'A'
        };
        _deleteFavouriteButton = new Button("Delete favourite",
            new DeleteFavouriteCommand(
                contentModelService,
                _listBox.ScrollableModel,
                _favourites), themeService)
        {
            HotKeyChar = 'D'
        };
        // The list goes in first on purpose: SetFocus lands on the first focusable control, and a
        // page that opens with "Add to favourites" focused answers the arrow keys with nothing and
        // Enter with the wrong verb.
        AddControl(_listBox);
        AddControl(_scrollBar);
        AddControl(_addToFavouritesButton);
        AddControl(_deleteFavouriteButton);
        AddControl(_selectFavouriteButton);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        StateHasChanged();
        // FullUrl, not the Url itself: Url does not override ToString, so joining the objects wrote
        // one "iConsole.Data.Url" per favourite into the settings and every saved favourite came
        // back as that string.
        _settingsService.Set(_key, string.Join(';', _favourites.AsSnapshot().Select(f => f.FullUrl)));
    }

    public override Task OnInitializeAsync()
    {
        // Read first, clear second. Clear raises CollectionChanged, which writes the (now empty)
        // list straight back over the setting — so clearing first meant the saved favourites were
        // erased a line before they were read, on every single start-up.
        var favourites = _settingsService.Get(_key);
        _favourites.Clear();
        if (favourites is not null)
        {
            foreach (var favourite in favourites.Split(';'))
            {
                // An empty entry is what a wiped setting splits into; a Url built from it points at
                // nothing and Enter on that row would navigate the panel to nowhere.
                if (!string.IsNullOrWhiteSpace(favourite)
                    && !_favourites.Any(f => f.FullUrl == favourite))
                {
                    _favourites.Add(new Url(favourite));
                }
            }
        }
        return base.OnInitializeAsync();
    }

    public override void OnInitializeLayout()
    {
        var innerWidth = Math.Max(0, (int)ViewSize.Width - 2);
        var listHeight = Math.Max(0, (int)ViewSize.Height - 3);
        var halfWidth = innerWidth / 2;

        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(innerWidth, listHeight);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(Math.Max(0, ViewSize.Width - 1), 1);
        _scrollBar.ViewSize = new Size(1, listHeight);

        _addToFavouritesButton.Position = new Position(1, Math.Max(0, ViewSize.Height - 2));
        _addToFavouritesButton.ViewSize = new Size(halfWidth, 1);

        _deleteFavouriteButton.Position = new Position(1 + halfWidth, Math.Max(0, ViewSize.Height - 2));
        _deleteFavouriteButton.ViewSize = new Size(innerWidth - halfWidth, 1);

        _selectFavouriteButton.Position = new Position(1, Math.Max(0, ViewSize.Height - 1));
        _selectFavouriteButton.ViewSize = new Size(innerWidth, 1);

        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        foreach (var hint in base.GetKeyHints())
        {
            yield return hint;
        }
        yield return ("Ctrl+A", "Add the folder the active panel is showing to the favourites.");
        yield return ("Ctrl+D", "Remove the favourite under the cursor.");
    }

    private IEnumerable<Url> FilterAndSortItems(IEnumerable<Url> items, CancellationToken cancellationToken)
    {
        return items;
    }

    private ListItem<Url> TransformItem(Url item, CancellationToken cancellationToken)
    {
        var transformedItem = new ListItem<Url>(item.FullUrl)
        {
            Value = item.FullUrl.Shrink((int)_listBox.ViewSize.Width - 2),
            IsEnabled = true
        };
        return transformedItem;
    }
}

