using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// Inline completion for a <see cref="TextBox"/>. It remains a logical child of the text box so it
/// keeps receiving Tab (complete) and Up/Down (navigate) through the normal child key routing, but
/// it renders its suggestion list in an owned, <b>non-modal</b> <see cref="Popup"/> so the list
/// floats above sibling controls instead of being clipped at the text box's layer. The list and
/// scrollbar live in that popup; the commands here hold their models directly, so routing keys to
/// this control still drives them. Visibility tracks whether any completion matches the current text.
///
/// The suggestion list is the transient sub-state <see cref="ITransientOverlayHost"/> exists for: Esc
/// takes the list down and leaves the text box alone, and only an Esc with no list up closes the
/// dialog. Typing brings it back, so dismissing costs nothing.
/// </summary>
public class AutoCompletion : Window, ITransientOverlayHost
{
    private readonly ITextModel _textModel;
    public ICompletionModel CompletionModel { get; }
    private readonly ListBox<string> _listBox;
    private readonly ScrollBar.ScrollBar _scrollBar;
    private readonly Popup _popup;

    private readonly object _updateGate = new();
    private CancellationTokenSource? _updateCts;
    private int _suppressVisibilityUpdates;
    private int _generation;

    /// <summary>The control the suggestion popup is anchored beneath (the owning text box).</summary>
    public IControl? Owner { get; set; }

    public AutoCompletion(ITextModel textModel, ICompletionModel completionModel, IThemeService themeService)
        : base(themeService)
    {
        _textModel = textModel;
        _textModel.PropertyChanged += OnTextModelPropertyChanged;

        CompletionModel = completionModel;

        var listModel = new ListBoxModel<string>(CompletionModel.MatchingValues);
        _listBox = new ListBox<string>(listModel, themeService);

        _scrollBar = new ScrollBar.ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel
        };

        // The suggestion list lives in a non-modal popup so it overlays siblings correctly while the
        // text box keeps focus and input. The list/scrollbar are the popup's children; the commands
        // below hold their models directly, so routing the keys to this control still works.
        _popup = new Popup(themeService)
        {
            Modal = false,
            HasBorder = true,
            CloseOnOutsideClick = false,
            CloseOnEscape = false,
            Placement = PopupPlacement.Below,
        };
        _popup.AddControl(_listBox);
        _popup.AddControl(_scrollBar);

        AddCommand(new AutoCompleteCommand(_textModel, CompletionModel));
        AddCommand(new ScrollUpCommand(_listBox.ScrollableModel));
        AddCommand(new ScrollDownCommand(_listBox.ScrollableModel));
    }

    /// <summary>The popup that hosts the suggestion list. Exposed for hosting and tests.</summary>
    public Popup ListPopup => _popup;

    public bool IsPopupOpen => _popup.IsOpen;

    /// <inheritdoc/>
    public bool IsOverlayOpen => _popup.IsOpen;

    /// <inheritdoc/>
    /// <remarks>
    /// The same dismissal Tab performs once it has completed: <see cref="Hide"/> closes the popup and
    /// clears this control's own visibility. The text is not touched, so nothing re-opens the list
    /// until the next keystroke — which is exactly the moment it becomes useful again.
    /// </remarks>
    public void CloseOverlay() => Hide();

    public void SuppressNextVisibilityUpdate()
    {
        Interlocked.Increment(ref _suppressVisibilityUpdates);
    }

    /// <summary>
    /// Returns the suggestion currently highlighted by the cursor in the list, or null if nothing
    /// is selected.
    /// </summary>
    public string? GetSelectedValue()
    {
        var cursorY = (int)_listBox.ScrollableModel.CursorPosition.Y;
        var items = _listBox.ListModel.ListItems;
        if (cursorY >= 0 && cursorY < items.Count)
        {
            return items[cursorY].Item;
        }
        return null;
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        UpdateMatches(_textModel.Lines.FirstOrDefault() ?? string.Empty, CurrentGeneration);
    }

    public override void Draw(IBuffer buffer)
    {
        // The suggestion list paints itself through the popup; this control is an invisible
        // logic/routing node and draws nothing of its own.
    }

    /// <summary>
    /// Shows the suggestion popup anchored beneath <see cref="Owner"/>. Idempotent, and only while
    /// the owner has the keyboard: matches are worked out as soon as the box initialises, and the
    /// list used to go up with them — for a box nobody was typing in, on a tab page that was not even
    /// showing. Opening the folder selector put the Path page's whole history over the dialog, and
    /// since the page was hidden nothing closed it: it stayed over the panels after the selector had
    /// gone.
    /// </summary>
    public async Task ShowPopupAsync()
    {
        // Only float the list when the owner is nested in a real tree: a root-attached popup needs an
        // enclosing root with screen space above the owner, otherwise placement clamps onto it.
        if (Owner is null || Owner.Parent is null || _popup.IsOpen || !Owner.IsFocused || !Owner.IsVisible)
        {
            return;
        }

        LayoutPopup();
        await _popup.ShowAsync(Owner);
    }

    /// <summary>
    /// Which round of matching is allowed to put the list on screen. Every dismissal moves it on, so a
    /// match update that was already in flight when the list was dismissed can no longer re-show it.
    /// </summary>
    private int CurrentGeneration => Volatile.Read(ref _generation);

    public override void Hide()
    {
        // A dismissal has to *stick*. Matching runs on a background task — two of them per keystroke
        // as a matter of course, one from the text-changed handler and one from the ShowAsync the same
        // keystroke triggers — so without this an Esc (or a page switch, or a completed Tab) landing
        // between the keystroke and the update it started was undone a few milliseconds later by that
        // update calling ApplyVisibility(true), and the list the user had just dismissed came back on
        // its own.
        //
        // Moving the generation on, rather than cancelling the update: the token is only checked
        // before the work starts, so cancelling cannot stop the update that is already past that
        // point — which is exactly the one that resurrects the list — while it *can* leave
        // `CompletionModel.UpdateTask` cancelled under everything that awaits it.
        // Under the same lock as the check in UpdateMatches, so the two cannot interleave.
        lock (_updateGate)
        {
            Interlocked.Increment(ref _generation);
            base.Hide();

            // Unconditionally, not only when IsOpen: the popup attaches itself to the root, so a show
            // that was interrupted before it finished has already put it there. Close is what detaches
            // it either way, and it is the only thing standing between a closed dialog and a suggestion
            // list left floating over the panels.
            _popup.Close();
        }
    }

    private void OnTextModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ITextModel.Lines), StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(e.PropertyName))
        {
            return;
        }

        var value = _textModel.Lines.FirstOrDefault() ?? string.Empty;

        CancellationToken token;
        lock (_updateGate)
        {
            _updateCts?.Cancel();
            _updateCts?.Dispose();
            _updateCts = new CancellationTokenSource();
            token = _updateCts.Token;
        }

        // Read before the task starts: this update belongs to the state the text was in when the key
        // was pressed, and a dismissal after that point is what it has to lose to.
        var generation = CurrentGeneration;

        CompletionModel.UpdateTask = Task.Run(() =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            UpdateMatches(value, generation);
        }, token);
    }

    private void UpdateMatches(string value, int generation)
    {
        CompletionModel.UpdateMatchingValues(value);

        _listBox.UpdateModel();
        if (_listBox.ListModel.UpdateTask is not null)
        {
            _listBox.ListModel.UpdateTask.Wait();
        }

        // The generation is checked and acted on under the lock Hide takes to move it on. Checking
        // outside it let a dismissal land in between: the check passed, Hide closed the list, and then
        // this update showed it again.
        lock (_updateGate)
        {
            var visible = CompletionModel.MatchingValues.Any()
                && generation == CurrentGeneration
                && Interlocked.Exchange(ref _suppressVisibilityUpdates, 0) == 0;
            ApplyVisibility(visible);
        }
    }

    private void ApplyVisibility(bool visible)
    {
        IsVisible = visible;
        if (visible)
        {
            _ = ShowPopupAsync();
        }
        else if (_popup.IsOpen)
        {
            _popup.Close();
        }
    }

    private void LayoutPopup()
    {
        var width = (int)(Owner?.ViewSize.Width ?? ViewSize.Width);
        if (width <= 0)
        {
            width = (int)ViewSize.Width;
        }

        var height = (int)ViewSize.Height;
        if (height <= 0)
        {
            height = 5;
        }

        _popup.ViewSize = new Size(width, height);

        var innerHeight = Math.Max(1, height - 2);
        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(Math.Max(1, width - 2), innerHeight);
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(Math.Max(1, width - 2), 1);
        _scrollBar.ViewSize = new Size(1, innerHeight);
    }
}
