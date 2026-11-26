using iConsole.Services;
using iTerminal.Core.Dialogs.Recipes;
using iTerminal.Core.Model.Recipes;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Recipes;

[TestClass]
public class RecipeEditDialogTest
{
    private IDisposable _clipboardScope = null!;

    [TestCleanup]
    public void ReleaseOwnClipboard() => _clipboardScope.Dispose();

    [TestInitialize]
    public void ScopeOwnClipboard() => _clipboardScope = TextClipboard.Scope(new InProcessTextClipboardService());

    private static RecipeEditDialog Create() =>
        new(new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, TestTheme());

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFreshFormIsEmptyAndShowsItsFields()
    {
        var dialog = Create();
        dialog.Open(null);

        await ShowAsync(dialog);

        Assert.AreEqual(string.Empty, dialog.TitleForTest);
        Assert.AreEqual(string.Empty, dialog.OriginForTest);
        var screen = Render(dialog);
        AssertScreenContains(screen, "Ingredients, one per line");
        AssertScreenContains(screen, "Steps, one per line");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SavingWithoutATitleSaysWhyAndSavesNothing()
    {
        var dialog = Create();
        dialog.Open(null);
        await ShowAsync(dialog);
        dialog.IngredientsEditorForTest.TextModel.Lines = ["1 egg"];
        dialog.StepsEditorForTest.TextModel.Lines = ["Fry."];
        var saved = false;
        dialog.Saved += _ => saved = true;

        dialog.Save();

        Assert.IsFalse(saved);
        StringAssert.Contains(dialog.ErrorForTest, "A recipe needs a title.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AValidRecipeIsHandedOverWithItsLinesTrimmed()
    {
        var dialog = Create();
        dialog.Open(null);
        dialog.TitleForTest = "  Omelette  ";
        dialog.OriginForTest = "France";
        dialog.IngredientsEditorForTest.TextModel.Lines = ["2 eggs", "", " butter "];
        dialog.StepsEditorForTest.TextModel.Lines = ["Whisk.", "Fry."];
        Recipe? saved = null;
        dialog.Saved += recipe => saved = recipe;

        dialog.Save();

        Assert.IsNotNull(saved);
        Assert.AreEqual("Omelette", saved.Title);
        Assert.AreEqual("France", saved.Origin);
        CollectionAssert.AreEqual(new[] { "2 eggs", "butter" }, saved.Ingredients.ToList());
        CollectionAssert.AreEqual(new[] { "Whisk.", "Fry." }, saved.Steps.ToList());
        Assert.IsFalse(saved.IsPreloaded);
        Assert.AreNotEqual(Guid.Empty, saved.Id);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ChangingARecipeKeepsItsIdentifier()
    {
        var existing = new Recipe(Guid.NewGuid(), "Old", "", ["a"], ["b"], IsPreloaded: false);
        var dialog = Create();
        dialog.Open(existing);
        Recipe? saved = null;
        dialog.Saved += recipe => saved = recipe;

        dialog.Save();

        Assert.AreEqual(existing.Id, saved?.Id);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterInAnEditorInsertsALineAndSavesNothing()
    {
        var dialog = Create();
        dialog.Open(null);
        dialog.IngredientsEditorForTest.TextModel.Lines = ["2 eggs"];
        var saved = false;
        dialog.Saved += _ => saved = true;

        await dialog.IngredientsEditorForTest.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual(2, dialog.IngredientsEditorForTest.TextModel.Lines.Count);
        Assert.IsFalse(saved);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TabInAnEditorIsHandedBackToTheForm()
    {
        var dialog = Create();
        dialog.Open(null);
        await ShowAsync(dialog);

        var handled = await dialog.IngredientsEditorForTest.HandleKeyEventAsync(Key(ConsoleKey.Tab, '\t'));

        Assert.IsFalse(handled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APastedLineBreakInATitleBecomesASpace()
    {
        var dialog = Create();
        dialog.Open(null);

        dialog.TitleBoxForTest.InsertText("Egg\nsalad");

        Assert.AreEqual("Egg salad", dialog.TitleForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALineBreakThatReachesTheModelIsRefusedWithTheReason()
    {
        var dialog = Create();
        dialog.Open(null);
        await ShowAsync(dialog);
        dialog.TitleForTest = "Egg\nsalad";
        dialog.IngredientsEditorForTest.TextModel.Lines = ["2 eggs"];
        dialog.StepsEditorForTest.TextModel.Lines = ["Fry."];
        var saved = false;
        dialog.Saved += _ => saved = true;

        dialog.Save();

        Assert.IsFalse(saved);
        StringAssert.Contains(dialog.ErrorForTest, "must fit on one line");
    }
}
