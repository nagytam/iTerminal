using iConsole.Data;
using iTerminal.Core.Dialogs.Recipes;

namespace iTerminal.Core.Commands;

public class RecipeManagerDialogCommand(RecipeManagerDialog recipeManagerDialog) : ShowDialogCommand<RecipeManagerDialog>(recipeManagerDialog)
{
    public override string Title => "Recipes";
    public override string Description => "Browse favourite recipes from Switzerland, Germany, France, Italy and Hungary, and keep your own with their ingredients and steps.";

    // No hot key: a new one is a decision for the person, so the recipes open from the Tools > Office menu and the command palette.
    public override Key? Key { get; init; } = null;
}
