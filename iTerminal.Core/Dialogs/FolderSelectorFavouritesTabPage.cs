using iConsole.Localization;
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

public class FolderSelectorFavouritesTabPage : FolderSelectorTabPage, ITransientOverlayHost
{
    private string _key = "Favourites";
    private ISettingsService _settingsService { get; set; }
    private ConcurrentObservableCollection<Url> _favourites { get; set; }
    private ListBox<Url> _listBox { get; set; }
    private ScrollBar _scrollBar { get; set; }
    private Button _addToFavouritesButton { get; set; }
    private Button _deleteFavouriteButton { get; set; }
    private Button _selectFavouriteButton { get; set; }
    private readonly Label _confirmLabel;
    private readonly DeleteFavouriteCommand _deleteFavouriteCommand;
    private int? _pendingDeleteIndex;
    private Url? _pendingDeleteUrl;
    public ScrollableModel ScrollableModel => _listBox.ScrollableModel;

    /// <summary>The favourites as they are listed, in list order. Exposed for the select command
    /// and for tests, which otherwise could not tell an empty list from an unread one.</summary>
    public IReadOnlyList<Url> Favourites => [.. _favourites.AsSnapshot()];

    private ICommand _selectFavouriteCommand { get; set; }
    protected override ICommand? SelectCommand => _selectFavouriteCommand;
    protected override string SelectDescription => iConsole.Localization.UiText.Get("FolderSelector.Select.Favourite", "Go to the favourite folder under the cursor.");

    public FolderSelectorFavouritesTabPage(
        IContentModelService contentModelService,
        ISettingsService settingsService,
        IThemeService themeService)
        : base(iConsole.Localization.UiText.Get("FolderSelector.Tab0", "0-Favourites"), ConsoleKey.D0, themeService)
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
            EmptyText = UiText.Get("Empty.Favourites", UiText.Get("Empty.Favourites", " No favourites yet — Add to favourites saves the folder the panel is showing.")),
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel
        };
        _selectFavouriteCommand = new SelectFavouriteCommand(
            contentModelService,
            _listBox.ScrollableModel,
            _favourites);
        _selectFavouriteButton = new Button(iConsole.Localization.UiText.Get("Button.SelectFavourite", "Select favourite"), _selectFavouriteCommand, themeService)
        {
            HotKeyChar = 'S'
        };
        _addToFavouritesButton = new Button(iConsole.Localization.UiText.Get("Button.AddToFavourites", "Add to favourites"),
            new AddToFavouritesCommand(
                contentModelService,
                _favourites), themeService)
        {
            HotKeyChar = 'A'
        };
        // Removing a favourite is not undoable, so the button and Ctrl+D only ask; the answer runs the delete.
        _deleteFavouriteCommand = new DeleteFavouriteCommand(
            contentModelService,
            _listBox.ScrollableModel,
            _favourites);
        _confirmLabel = new Label(string.Empty, themeService);
        _deleteFavouriteButton = new Button(iConsole.Localization.UiText.Get("Button.DeleteFavourite", "Delete favourite"),
            new BeginDeleteFavouriteCommand(), themeService)
        {
            HotKeyChar = iConsole.Localization.HotKeys.VisibleIn(iConsole.Localization.UiText.Get("Button.DeleteFavourite", "Delete favourite"), 'D'),
            IsDestructive = true
        };
        // The list goes in first on purpose: SetFocus lands on the first focusable control, and a
        // page that opens with "Add to favourites" focused answers the arrow keys with nothing and
        // Enter with the wrong verb.
        AddControl(_listBox);
        AddControl(_scrollBar);
        AddControl(_confirmLabel);
        AddControl(_addToFavouritesButton);
        AddControl(_deleteFavouriteButton);
        AddControl(_selectFavouriteButton);
    }

    /// <summary>
    /// Asks, on the status line above the buttons, whether the favourite under the cursor goes. Nothing is removed
    /// until <c>y</c>; <c>n</c>, Enter or any other key drops the question.
    /// </summary>
    internal void BeginDelete()
    {
        var index = (int)_listBox.ScrollableModel.CursorPosition.Y;
        if (index < 0 || index >= _favourites.Count)
        {
            return;
        }
        var favourite = _favourites.AsSnapshot()[index];
        _pendingDeleteIndex = index;
        _pendingDeleteUrl = favourite;
        // The question has to fit the status line whole, so the name gives up the room the rest of the question needs.
        var question = iConsole.Localization.UiText.Get("FolderSelector.Favourites.ConfirmRemove", "Remove {0} from the favourites?  y:yes  n:no");
        var room = Math.Max(10, (int)ViewSize.Width - 2 - (question.Length - "{0}".Length));
        _confirmLabel.Text = string.Format(question, favourite.FullUrl.Shrink(room));
    }

    public bool IsOverlayOpen => _pendingDeleteIndex is not null;

    /// <summary>Esc takes the question down and leaves the selector open; it is the first of the two Esc presses.</summary>
    public void CloseOverlay() => CancelDelete();

    private void CancelDelete()
    {
        _pendingDeleteIndex = null;
        _pendingDeleteUrl = null;
        _confirmLabel.Text = string.Empty;
    }

    /// <summary>The question on the status line, for a test to read.</summary>
    internal string ConfirmTextForTest => _confirmLabel.Text;

    /// <summary>Whether a delete is waiting for its y or n, for a test to read.</summary>
    internal bool IsAskingToDeleteForTest => _pendingDeleteIndex is not null;

    private async Task AnswerDelete(bool yes)
    {
        var pending = _pendingDeleteIndex;
        var pendingUrl = _pendingDeleteUrl;
        CancelDelete();
        // The delete acts on the cursor, so it runs only if the cursor still rests on the favourite the question named.
        if (yes && pending is int index && pendingUrl is not null
            && (int)_listBox.ScrollableModel.CursorPosition.Y == index
            && index < _favourites.Count && _favourites.AsSnapshot()[index].FullUrl == pendingUrl.FullUrl)
        {
            await _deleteFavouriteCommand.RunAsync(null);
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_pendingDeleteIndex is not null)
        {
            // Shift is allowed so that Y answers yes too; Ctrl and Alt combinations are not answers.
            var plain = !keyEvent.Control && !keyEvent.Alt;
            var answer = char.ToLowerInvariant(keyEvent.KeyChar);
            if (plain && (answer is 'y' or 'n' || (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Shift)))
            {
                await AnswerDelete(answer == 'y');
                return true;
            }
            // Any other key drops the question and then does what it does.
            await AnswerDelete(false);
        }
        return await base.HandleKeyEventAsync(keyEvent);
    }

    private sealed class BeginDeleteFavouriteCommand : Command
    {
        public override string Title => iConsole.Localization.UiText.Title("Delete a favourite");

        public override bool CanExecute() => Control?.Parent is FolderSelectorFavouritesTabPage page && page._favourites.Any();

        public override Task ExecuteAsync(object? args)
        {
            if (Control?.Parent is FolderSelectorFavouritesTabPage page)
            {
                page.BeginDelete();
            }
            return Task.CompletedTask;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        StateHasChanged();
        // FullUrl, not the Url itself: Url does not override ToString, so joining the objects wrote
        // one "iConsole.Data.Url" per favourite into the settings and every saved favourite came
        // back as that string.
        _settingsService.Set(_key, FolderListCodec.Join(_favourites.AsSnapshot().Select(f => f.FullUrl)));
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
            foreach (var favourite in FolderListCodec.Split(favourites))
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
        var listHeight = Math.Max(0, (int)ViewSize.Height - 4);
        var halfWidth = innerWidth / 2;

        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(innerWidth, listHeight);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(Math.Max(0, ViewSize.Width - 1), 1);
        _scrollBar.ViewSize = new Size(1, listHeight);

        _confirmLabel.Position = new Position(1, Math.Max(0, ViewSize.Height - 3));
        _confirmLabel.ViewSize = new Size(innerWidth, 1);

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
        yield return ("Ctrl+A", iConsole.Localization.UiText.Get("FolderSelector.Hint.AddFavourite", "Add the folder the active panel is showing to the favourites."));
        yield return ("Ctrl+D", iConsole.Localization.UiText.Get("FolderSelector.Hint.RemoveFavourite", "Remove the favourite under the cursor."));
    }

    private static IEnumerable<Url> FilterAndSortItems(IEnumerable<Url> items, CancellationToken cancellationToken)
    {
        return items;
    }

    private ListItem<Url> TransformItem(Url item, CancellationToken cancellationToken)
    {
        var transformedItem = new ListItem<Url>(item.FullUrl)
        {
            Value = item.FullUrl.Shrink((int)_listBox.ViewSize.Width - 2)!,
            IsEnabled = true
        };
        return transformedItem;
    }
}

