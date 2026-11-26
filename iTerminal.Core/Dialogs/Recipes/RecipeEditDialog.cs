using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model.Recipes;

namespace iTerminal.Core.Dialogs.Recipes;

/// <summary>
/// The form that adds or changes a custom recipe: a title, an origin, and the ingredients and steps one per line. Save is
/// the button, reached with Tab, so Enter in an editor inserts a line break. Esc closes without saving. The checks are
/// those of <see cref="Recipe.Check"/>, and a valid recipe is handed to <see cref="Saved"/> before the form closes.
/// </summary>
public class RecipeEditDialog : FormDialog
{
    private const int DialogWidth = 72;
    private const int DialogHeight = 22;
    private const int ErrorRows = 2;
    private const int IngredientRows = 5;
    private const int StepRows = 6;

    private readonly TextBox _title;
    private readonly TextBox _origin;
    private readonly Label _ingredientsLabel;
    private readonly LineEditor _ingredients;
    private readonly Label _stepsLabel;
    private readonly LineEditor _steps;
    private readonly Label[] _errorLabels;
    private readonly Button _saveButton;
    private readonly Button _cancelButton;

    /// <summary>
    /// A multi-line editor that keeps the position it is placed at. The editor centres itself in its parent on every layout,
    /// which would pile the two editors on top of each other. It also takes Tab for indentation, so Tab is handed back to the
    /// form, which moves between the fields.
    /// </summary>
    private sealed class LineEditor(IThemeService themeService) : TextEditorControl(themeService)
    {
        public override void OnInitializeLayout()
        {
            Position placed = Position;
            base.OnInitializeLayout();
            Position = placed;
        }

        public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.Tab && !keyEvent.Control && !keyEvent.Alt)
            {
                return false;
            }

            return await base.HandleKeyEventAsync(keyEvent);
        }
    }

    /// <summary>The identifier of the recipe being changed, or null for a new one.</summary>
    private Guid? _editingId;

    public RecipeEditDialog(IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService)
        : base(nameof(RecipeEditDialog), themeService)
    {
        Title = UiText.Get("Dialog.RecipeEdit", "Recipe");
        _title = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Field.RecipeTitle", "Title") };
        _origin = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Field.RecipeOrigin", "Origin") };
        _ingredientsLabel = new Label(UiText.Get("Field.RecipeIngredients", "Ingredients, one per line"), themeService);
        _ingredients = new LineEditor(themeService) { CanFocus = true };
        _stepsLabel = new Label(UiText.Get("Field.RecipeSteps", "Steps, one per line"), themeService);
        _steps = new LineEditor(themeService) { CanFocus = true };
        _errorLabels = [.. Enumerable.Range(0, ErrorRows).Select(_ => new Label(string.Empty, themeService))];

        var saveCommand = new SaveRecipeCommand();
        AddCommand(saveCommand);
        _saveButton = new Button(UiText.Get("Common.Save", "Save"), saveCommand, themeService);
        _cancelButton = new CancelDialogButton(themeService);

        AddControl(_title);
        AddControl(_origin);
        AddControl(_ingredientsLabel);
        AddControl(_ingredients);
        AddControl(_stepsLabel);
        AddControl(_steps);
        foreach (var label in _errorLabels)
        {
            AddControl(label);
        }

        AddControl(_saveButton);
        AddControl(_cancelButton);
    }

    /// <summary>Raised with the valid recipe when Save is pressed, before the form closes. The manager stores it.</summary>
    public event Action<Recipe>? Saved;

    /// <summary>Shows the form for a new recipe (<paramref name="existing"/> null, every field empty) or for a custom one to change.</summary>
    public void Open(Recipe? existing)
    {
        _editingId = existing?.Id;
        _title.Text = existing?.Title ?? string.Empty;
        _origin.Text = existing?.Origin ?? string.Empty;
        SetLines(_ingredients, existing?.Ingredients ?? []);
        SetLines(_steps, existing?.Steps ?? []);
        SetError(string.Empty);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        foreach (var hint in base.GetKeyHints())
        {
            yield return hint;
        }

        yield return ("Enter", UiText.Get("DialogHint.RecipeEnter", "Save on the Save button; in an editor, a new line"));
        yield return ("Esc", UiText.Get("DialogHint.RecipeCancel", "Close without saving"));
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        if (Parent is not null)
        {
            CenterInParent(new Size(DialogWidth, DialogHeight));
        }

        var fieldWidth = FieldWidth;
        _title.Position = new Position(FieldX, 1);
        _title.ViewSize = new Size(fieldWidth, 1);
        _origin.Position = new Position(FieldX, 2);
        _origin.ViewSize = new Size(fieldWidth, 1);
        _ingredientsLabel.Position = new Position(FieldX, 3);
        _ingredientsLabel.ViewSize = new Size(fieldWidth, 1);
        _ingredients.Position = new Position(FieldX, 4);
        _ingredients.ViewSize = new Size(fieldWidth, IngredientRows);
        _stepsLabel.Position = new Position(FieldX, 4 + IngredientRows);
        _stepsLabel.ViewSize = new Size(fieldWidth, 1);
        _steps.Position = new Position(FieldX, 5 + IngredientRows);
        _steps.ViewSize = new Size(fieldWidth, StepRows);
        for (var i = 0; i < _errorLabels.Length; i++)
        {
            _errorLabels[i].Position = new Position(FieldX, 6 + IngredientRows + StepRows + i);
            _errorLabels[i].ViewSize = new Size(fieldWidth, 1);
        }

        // The editors centre themselves on every layout, so the position is set again after the base call.
        LayoutButtonRow(_saveButton, _cancelButton);
    }

    protected override void ClearError() => SetError(string.Empty);

    #region Test seams

    internal TextBox TitleBoxForTest => _title;
    internal TextEditorControl IngredientsEditorForTest => _ingredients;
    internal TextEditorControl StepsEditorForTest => _steps;
    internal string TitleForTest { get => _title.Text ?? string.Empty; set => _title.Text = value; }
    internal string OriginForTest { get => _origin.Text ?? string.Empty; set => _origin.Text = value; }
    internal string ErrorForTest => string.Join(" ", _errorLabels.Select(label => label.Text).Where(text => text.Length > 0));

    #endregion

    /// <summary>Checks the form and, when the recipe is valid, hands it to <see cref="Saved"/> and closes.</summary>
    public void Save()
    {
        var recipe = new Recipe(
            _editingId ?? Guid.NewGuid(),
            _title.Text ?? string.Empty,
            _origin.Text ?? string.Empty,
            GetLines(_ingredients),
            GetLines(_steps),
            IsPreloaded: false).Trimmed();

        if (recipe.Check() is { } problem)
        {
            SetError(Words(problem));
            return;
        }

        Saved?.Invoke(recipe);
        Close();
    }

    private void SetError(string message)
    {
        var lines = message.Length == 0 ? [] : message.WrapWords((int)Math.Max(10, FieldWidth));
        for (var i = 0; i < _errorLabels.Length; i++)
        {
            _errorLabels[i].Text = i < lines.Count ? lines[i] : string.Empty;
        }

        StateHasChanged();
    }

    private static string Words(RecipeProblem problem) => problem switch
    {
        RecipeProblem.TitleRequired => UiText.Get("Recipe.ErrTitle", "A recipe needs a title."),
        RecipeProblem.IngredientRequired => UiText.Get("Recipe.ErrIngredient", "A recipe needs at least one ingredient."),
        RecipeProblem.StepRequired => UiText.Get("Recipe.ErrStep", "A recipe needs at least one step."),
        RecipeProblem.LineBreak => UiText.Get("Recipe.ErrLineBreak", "Each title, origin, ingredient and step must fit on one line."),
        RecipeProblem.TooLong => UiText.Get("Recipe.ErrTooLong", "Text is too long: a title is at most 256 characters, an origin 128, and an ingredient or step 1000."),
        RecipeProblem.TooMany => UiText.Get("Recipe.ErrTooMany", "A recipe has at most 100 ingredients and 100 steps."),
        _ => string.Empty,
    };

    private static void SetLines(TextEditorControl editor, IReadOnlyList<string> items) =>
        editor.TextModel.Lines = items.Count == 0 ? [string.Empty] : [.. items];

    private static IReadOnlyList<string> GetLines(TextEditorControl editor) =>
        Recipe.Lines(string.Join('\n', editor.TextModel.Lines.AsSnapshot()));

    /// <summary>Save, from the button. The form decides; the command only asks it to.</summary>
    private sealed class SaveRecipeCommand : Command
    {
        public override string Title => "Save";
        public override string Description => "Save the recipe.";

        public override Key? Key { get; init; } = null;

        public override Task ExecuteAsync(object? args)
        {
            var dialog = Control as RecipeEditDialog ?? Control?.Parent as RecipeEditDialog;
            dialog?.Save();
            return Task.CompletedTask;
        }
    }
}
