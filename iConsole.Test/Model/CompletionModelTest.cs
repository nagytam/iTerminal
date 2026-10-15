using iConsole.Model;

namespace iConsole.Test.Model;

[TestClass]
public class CompletionModelTest
{
    [TestMethod]
    public void CompletionModelConstructor()
    {
        var completions = new[] { "apple", "application", "apply" };
        var model = new CompletionModel(completions);
        Assert.IsNotNull(model);
        Assert.AreEqual(3, model.Completions.Count());
    }

    [TestMethod]
    public void CompletionModelCompletions()
    {
        var completions = new[] { "apple", "application", "apply" };
        var model = new CompletionModel(completions);

        var list = model.Completions.ToList();
        Assert.AreEqual("apple", list[0]);
        Assert.AreEqual("application", list[1]);
        Assert.AreEqual("apply", list[2]);
    }

    [TestMethod]
    public void CompletionModelInsertCompletion()
    {
        var completions = new[] { "apple" };
        var model = new CompletionModel(completions);

        model.InsertCompletion("banana");

        Assert.AreEqual(2, model.Completions.Count());
        Assert.IsTrue(model.Completions.Contains("banana"));
    }

    [TestMethod]
    public void CompletionModelUpdateMatchingValuesAll()
    {
        var completions = new[] { "apple", "application", "apply" };
        var model = new CompletionModel(completions);

        model.UpdateMatchingValues("");

        Assert.AreEqual(3, model.MatchingValues.Count);
    }

    [TestMethod]
    public void CompletionModelUpdateMatchingValuesFiltered()
    {
        var completions = new[] { "apple", "application", "apply", "banana" };
        var model = new CompletionModel(completions);

        model.UpdateMatchingValues("app");

        Assert.AreEqual(3, model.MatchingValues.Count);
        Assert.IsTrue(model.MatchingValues.Contains("apple"));
        Assert.IsTrue(model.MatchingValues.Contains("application"));
        Assert.IsTrue(model.MatchingValues.Contains("apply"));
    }

    [TestMethod]
    public void CompletionModelUpdateMatchingValuesCaseless()
    {
        var completions = new[] { "Apple", "BANANA", "cherry" };
        var model = new CompletionModel(completions);

        model.UpdateMatchingValues("apple");

        Assert.AreEqual(1, model.MatchingValues.Count);
        Assert.IsTrue(model.MatchingValues.Contains("Apple"));
    }

    [TestMethod]
    public void CompletionModelUpdateMatchingValuesNoMatches()
    {
        var completions = new[] { "apple", "application" };
        var model = new CompletionModel(completions);

        model.UpdateMatchingValues("xyz");

        Assert.AreEqual(0, model.MatchingValues.Count);
    }

    [TestMethod]
    public void CompletionModelUpdateMatchingValuesClear()
    {
        var completions = new[] { "apple", "application" };
        var model = new CompletionModel(completions);

        model.UpdateMatchingValues("app");
        Assert.AreEqual(2, model.MatchingValues.Count);

        model.UpdateMatchingValues("xyz");
        Assert.AreEqual(0, model.MatchingValues.Count);
    }

    [TestMethod]
    public void CompletionModelMatchingValuesEmpty()
    {
        var completions = new[] { "apple" };
        var model = new CompletionModel(completions);

        Assert.AreEqual(0, model.MatchingValues.Count);
    }

    [TestMethod]
    public void CompletionModelInterface()
    {
        var completions = new[] { "apple" };
        var model = new CompletionModel(completions);

        Assert.IsInstanceOfType(model, typeof(ICompletionModel));
    }
}
