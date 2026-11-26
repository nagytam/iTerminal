using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model.Recipes;
using iTerminal.Core.Services.Recipes;

namespace iTerminal.Core.Dialogs.Recipes;

/// <summary>
/// The recipe manager: the favourite recipes of five countries, and the custom ones the person saves. The list is a table,
/// the highlighted recipe's ingredients and steps are shown under it. N adds a recipe and E changes the highlighted one,
/// both in <see cref="RecipeEditDialog"/>; D or Delete removes it after a y/n. The preloaded recipes cannot be changed or
/// removed, and E or D on one says so in the footer.
/// </summary>
public class RecipeManagerDialog : Dialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 22;
    private const int MaxListRows = 6;
    private const int TitleColumnFloor = 20;
    private const int OriginColumn = 18;
    private const int KindColumn = 10;

    /// <summary>What a background load or save has to say, applied on the next idle pass.</summary>
    private sealed record Outcome(IReadOnlyList<Recipe>? Recipes, Notice Notice, string? Error);

    /// <summary>The message the footer shows after a background operation, worded in the UI thread.</summary>
    private enum Notice
    {
        None,
        Saved,
        Deleted,
    }

    private readonly IRecipeService _recipeService;
    private readonly Func<RecipeEditDialog> _newEditDialog;

    private readonly ConcurrentObservableCollection<Recipe> _rows = [];
    private readonly Table<Recipe> _table;
    private readonly TextViewerControl _detail;

    // The recipes as the table shows them, read on the UI thread only; SyncRows copies them across.
    private IReadOnlyList<Recipe> _current = [];
    private Guid? _selectedId;

    /// <summary>The custom recipe a y/n is waiting on, or null. Its name is in the footer.</summary>
    private Recipe? _confirmingDelete;

    /// <summary>Bumped by each load, save or delete. A result from an older one is dropped, so only the newest lands.</summary>
    private int _generation;

    /// <summary>The result of the newest background operation, waiting for the UI thread. Guarded by <see cref="_gate"/>.</summary>
    private Outcome? _parked;

    private readonly Lock _gate = new();

    private string _status = string.Empty;
    private bool _statusIsError;

    public RecipeManagerDialog(IRecipeService recipeService, Func<RecipeEditDialog> newEditDialog, IThemeService themeService)
        : base(nameof(RecipeManagerDialog), themeService)
    {
        Title = UiText.Get("Dialog.Recipes", "Recipes");
        _recipeService = recipeService;
        _newEditDialog = newEditDialog;

        _table = new Table<Recipe>(_rows, themeService);
        ConfigureColumns();

        _detail = new TextViewerControl(false, themeService);
        AddControl(_table);
        AddControl(_detail);
    }

    /// <summary>The load the dialog started last; it exists so a test can wait for it instead of polling.</summary>
    internal Task LoadTask { get; private set; } = Task.CompletedTask;

    /// <summary>The save or delete started last; it exists so a test can wait for it instead of polling.</summary>
    internal Task OperationTask { get; private set; } = Task.CompletedTask;

    #region Test seams

    internal IReadOnlyList<Recipe> RecipesForTest => _current;
    internal string StatusForTest => _status;
    internal bool ConfirmingDeleteForTest => _confirmingDelete is not null;

    #endregion

    private int ListRows => Math.Clamp((int)ViewSize.Height - 2 - 9, 3, MaxListRows);

    private Recipe? Selected => _current.FirstOrDefault(r => r.Id == _selectedId) ?? _current.FirstOrDefault();

    /// <summary>Re-reads the recipes each time the dialog opens, since the database may have changed since.</summary>
    public override async Task ShowAsync()
    {
        _confirmingDelete = null;
        SetStatus(string.Empty, false);
        Reload();
        await base.ShowAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(DialogWidth, DialogHeight));
        }

        _table.Position = new Position(1, 1);
        _table.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), ListRows + 1);
        _table.ScrollableModel.ViewSize = new Size(_table.ViewSize.Width, ListRows);

        var detailTop = ListRows + 2;
        _detail.Position = new Position(1, detailTop);
        _detail.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), Math.Max(0, ViewSize.Height - 3 - detailTop));

        ConfigureColumns();
        SyncRows();
        base.OnInitializeLayout();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        ApplyParked();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Up/Down", UiText.Get("DialogHint.RecipeBrowse", "Move over the recipes"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.MovePageAtATime", "Move a page at a time"));
        yield return ("Home/End", UiText.Get("DialogHint.RecipeFirstLast", "Jump to the first or last recipe"));
        yield return ("N", UiText.Get("DialogHint.RecipeNew", "Add a recipe of your own"));
        yield return ("E", UiText.Get("DialogHint.RecipeEdit", "Change your recipe (not the preloaded ones)"));
        yield return ("D/Delete", UiText.Get("DialogHint.RecipeDelete", "Delete your recipe (asks first)"));
        yield return ("Esc", UiText.Get("DialogHint.RecipeClose", "Close the recipes"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_confirmingDelete is { } doomed)
        {
            // Esc is the dialog's own: it leaves the question as it closes. Any other key but y withdraws it.
            if (keyEvent.ConsoleKey != ConsoleKey.Escape)
            {
                _confirmingDelete = null;
                if (keyEvent.ConsoleKey == ConsoleKey.Y && !keyEvent.Control && !keyEvent.Alt)
                {
                    DeleteRecipe(doomed);
                }

                StateHasChanged();
                return true;
            }

            _confirmingDelete = null;
        }

        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow when plain:
                return MoveSelection(-1);
            case ConsoleKey.DownArrow when plain:
                return MoveSelection(1);
            case ConsoleKey.PageUp when plain:
                return MoveSelection(-ListRows);
            case ConsoleKey.PageDown when plain:
                return MoveSelection(ListRows);
            case ConsoleKey.Home when plain:
                return SelectAt(0);
            case ConsoleKey.End when plain:
                return SelectAt(_current.Count - 1);
            case ConsoleKey.Delete when plain:
                AskToDelete();
                return true;
        }

        // The letters ignore Shift: Caps Lock or Shift+N still means N.
        if (!keyEvent.Control && !keyEvent.Alt)
        {
            switch (char.ToLowerInvariant(keyEvent.KeyChar))
            {
                case 'n':
                    await OpenEditorAsync(null);
                    return true;
                case 'e':
                    await OpenEditorAsync(Selected);
                    return true;
                case 'd':
                    AskToDelete();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>Starts a read of the recipes on a background task; its result is parked for the next idle pass.</summary>
    private void Reload()
    {
        var generation = NewOperation();
        LoadTask = LoadAsync(generation);
    }

    private async Task LoadAsync(int generation)
    {
        try
        {
            var recipes = await _recipeService.GetAllAsync();
            Park(generation, new Outcome(recipes, Notice.None, null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Park(generation, new Outcome(null, Notice.None, exception.Message));
        }
    }

    /// <summary>Saves the recipe from the edit form on a background task, then reads the recipes again.</summary>
    private void SaveRecipe(Recipe recipe)
    {
        var generation = NewOperation();
        _selectedId = recipe.Id;
        OperationTask = SaveAsync(recipe, generation);
    }

    private async Task SaveAsync(Recipe recipe, int generation)
    {
        try
        {
            await _recipeService.SaveAsync(recipe);
            var recipes = await _recipeService.GetAllAsync();
            Park(generation, new Outcome(recipes, Notice.Saved, null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Park(generation, new Outcome(null, Notice.None, exception.Message));
        }
    }

    private void DeleteRecipe(Recipe recipe)
    {
        var generation = NewOperation();
        _selectedId = null;
        OperationTask = DeleteAsync(recipe.Id, generation);
    }

    private async Task DeleteAsync(Guid id, int generation)
    {
        try
        {
            await _recipeService.DeleteAsync(id);
            var recipes = await _recipeService.GetAllAsync();
            Park(generation, new Outcome(recipes, Notice.Deleted, null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Park(generation, new Outcome(null, Notice.None, exception.Message));
        }
    }

    /// <summary>
    /// Starts a new operation: takes the next generation and drops any result parked for an older one, so an older
    /// result that has not been applied yet cannot land after this one has started.
    /// </summary>
    private int NewOperation()
    {
        lock (_gate)
        {
            _parked = null;
            return ++_generation;
        }
    }

    /// <summary>Keeps a result only while it is still the newest operation; an older one is dropped, not shown late.</summary>
    private void Park(int generation, Outcome outcome)
    {
        lock (_gate)
        {
            if (generation == _generation)
            {
                _parked = outcome;
            }
        }
    }

    private void ApplyParked()
    {
        Outcome? outcome;
        lock (_gate)
        {
            outcome = _parked;
            _parked = null;
        }

        if (outcome is null)
        {
            return;
        }

        if (outcome.Recipes is not null)
        {
            _current = outcome.Recipes;
            _rows.ReplaceAll(_current.ToList());
            SyncRows();
        }

        if (outcome.Error is not null)
        {
            SetStatus(string.Format(UiText.Get("Recipe.Failed", "Could not read or save the recipes: {0}"), outcome.Error), true);
            return;
        }

        switch (outcome.Notice)
        {
            case Notice.Saved:
                SetStatus(UiText.Get("Recipe.Saved", "Recipe saved."), false);
                break;
            case Notice.Deleted:
                SetStatus(UiText.Get("Recipe.Deleted", "Recipe deleted."), false);
                break;
        }

        StateHasChanged();
    }

    /// <summary>Opens the edit form for a new recipe, or for a custom one to change. A preloaded one is refused in the footer.</summary>
    private async Task OpenEditorAsync(Recipe? existing)
    {
        if (existing is { IsPreloaded: true })
        {
            SetStatus(UiText.Get("Recipe.PreloadedReadOnly", "Preloaded recipes cannot be changed."), true);
            return;
        }

        var dialog = _newEditDialog();
        dialog.Open(existing);
        dialog.Saved += SaveRecipe;
        await ShowDialogAsync(dialog);
    }

    private void AskToDelete()
    {
        if (Selected is not { } recipe)
        {
            return;
        }

        if (recipe.IsPreloaded)
        {
            SetStatus(UiText.Get("Recipe.PreloadedReadOnly", "Preloaded recipes cannot be changed."), true);
            return;
        }

        _confirmingDelete = recipe;
        StateHasChanged();
    }

    private bool MoveSelection(int delta)
    {
        if (_current.Count == 0)
        {
            return true;
        }

        var index = IndexOfSelected();
        return SelectAt(Math.Clamp(index + delta, 0, _current.Count - 1));
    }

    private bool SelectAt(int index)
    {
        if (_current.Count == 0)
        {
            return true;
        }

        _selectedId = _current[Math.Clamp(index, 0, _current.Count - 1)].Id;
        SyncRows();
        StateHasChanged();
        return true;
    }

    private int IndexOfSelected()
    {
        var index = _current.ToList().FindIndex(r => r.Id == _selectedId);
        return index < 0 ? 0 : index;
    }

    /// <summary>Puts the recipes on the table, moves the highlight to the selected one, and shows its details below.</summary>
    private void SyncRows()
    {
        if (_current.Count > 0 && !_current.Any(r => r.Id == _selectedId))
        {
            _selectedId = _current[0].Id;
        }

        var model = _table.ScrollableModel;
        model.CursorPosition.Y = IndexOfSelected();
        if (model.ViewSize.Height > 0)
        {
            model.ScrollToVisible();
        }

        model.Update();
        _detail.TextModel.Lines = [.. DetailLines(Selected)];
    }

    private static IEnumerable<string> DetailLines(Recipe? recipe)
    {
        if (recipe is null)
        {
            yield break;
        }

        if (recipe.Origin.Length > 0)
        {
            yield return string.Format(UiText.Get("Recipe.OriginLine", "From {0}"), recipe.Origin);
        }

        yield return string.Empty;
        yield return UiText.Get("Recipe.Ingredients", "Ingredients");
        foreach (var ingredient in recipe.Ingredients)
        {
            yield return "  • " + ingredient;
        }

        yield return string.Empty;
        yield return UiText.Get("Recipe.Steps", "Steps");
        var number = 1;
        foreach (var step in recipe.Steps)
        {
            yield return $"  {number++}. {step}";
        }
    }

    private void ConfigureColumns()
    {
        var titleWidth = Math.Max(TitleColumnFloor, (int)_table.ViewSize.Width - OriginColumn - KindColumn - 2);
        _table.Columns.Clear();
        _table.Columns.Add(new ColumnDefinition<Recipe>(UiText.Get("Field.RecipeTitle", "Title"), titleWidth, r => r.Title));
        _table.Columns.Add(new ColumnDefinition<Recipe>(UiText.Get("Field.RecipeOrigin", "Origin"), OriginColumn, r => r.Origin));
        _table.Columns.Add(new ColumnDefinition<Recipe>(UiText.Get("Recipe.ColKind", "Kind"), KindColumn, r => r.IsPreloaded
            ? UiText.Get("Recipe.Preloaded", "Preloaded")
            : UiText.Get("Recipe.Own", "Your own")));
    }

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
        StateHasChanged();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        for (var row = 0; row < ih; row++)
        {
            Put(buffer, 0, row, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
        }

        var count = string.Format(UiText.Get("Recipe.Count", "{0} recipes"), _current.Count);
        Put(buffer, iw - count.Length - 1, 0, count.Length, count, colorScheme.DisabledForeground, colorScheme.Background);

        var promptRow = ih - 2;
        if (_confirmingDelete is { } doomed)
        {
            Put(buffer, 1, promptRow, iw - 2, string.Format(UiText.Get("Prompt.DeleteRecipe", "Delete \"{0}\"? (y/n)"), doomed.Title), colorScheme.Error, colorScheme.Background);
        }
        else
        {
            Put(buffer, 1, promptRow, iw - 2, _status, _statusIsError ? colorScheme.Error : colorScheme.Foreground, colorScheme.Background);
        }

        Put(buffer, 1, ih - 1, iw - 2, UiText.Get("Recipe.Hint", "Up/Down move  N new  E change  D delete  Esc close").FitHints(iw - 2), colorScheme.Foreground, colorScheme.Background);
    }

    private void Put(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background)
    {
        if (width <= 0)
        {
            return;
        }

        var clipped = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1 + x, AbsolutePosition.Y + 1 + y),
            new Size(width, 1),
            new ConxelString(clipped, foreground, background));
    }
}
