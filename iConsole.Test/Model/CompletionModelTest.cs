using System.Collections.Specialized;
using iConsole.Model;

namespace iConsole.Test.Model;

[TestClass]
public class CompletionModelTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelConstructor()
    {
        var completions = new[] { "apple", "application", "apply" };
        var model = new CompletionModel(completions);
        Assert.IsNotNull(model);
        Assert.AreEqual(3, model.Completions.Count());
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelInsertCompletion()
    {
        var completions = new[] { "apple" };
        var model = new CompletionModel(completions);

        model.InsertCompletion("banana");

        Assert.AreEqual(2, model.Completions.Count());
        Assert.IsTrue(model.Completions.Contains("banana"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelUpdateMatchingValuesAll()
    {
        var completions = new[] { "apple", "application", "apply" };
        var model = new CompletionModel(completions);

        model.UpdateMatchingValues("");

        Assert.AreEqual(3, model.MatchingValues.Count);
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelUpdateMatchingValuesCaseless()
    {
        var completions = new[] { "Apple", "BANANA", "cherry" };
        var model = new CompletionModel(completions);

        model.UpdateMatchingValues("apple");

        Assert.AreEqual(1, model.MatchingValues.Count);
        Assert.IsTrue(model.MatchingValues.Contains("Apple"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelUpdateMatchingValuesNoMatches()
    {
        var completions = new[] { "apple", "application" };
        var model = new CompletionModel(completions);

        model.UpdateMatchingValues("xyz");

        Assert.AreEqual(0, model.MatchingValues.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelUpdateMatchingValuesFallsBackToFuzzyWhenNoPrefixMatches()
    {
        var model = new CompletionModel(["application", "banana", "cherry"]);

        model.UpdateMatchingValues("aplcn");

        CollectionAssert.AreEqual(new[] { "application" }, model.MatchingValues.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelUpdateMatchingValuesPrefixMatchesSuppressFuzzy()
    {
        var model = new CompletionModel(["apple", "a_p_p_le_tree"]);

        model.UpdateMatchingValues("app");

        CollectionAssert.AreEqual(new[] { "apple" }, model.MatchingValues.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelUpdateMatchingValuesShortInputDoesNotFuzzy()
    {
        var model = new CompletionModel(["banana"]);

        model.UpdateMatchingValues("bn");

        Assert.AreEqual(0, model.MatchingValues.Count);
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelMatchingValuesEmpty()
    {
        var completions = new[] { "apple" };
        var model = new CompletionModel(completions);

        Assert.AreEqual(0, model.MatchingValues.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelInterface()
    {
        var completions = new[] { "apple" };
        var model = new CompletionModel(completions);

        Assert.IsInstanceOfType(model, typeof(ICompletionModel));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelUpdateMatchingValuesRaisesOneChangePerUpdate()
    {
        var completions = Enumerable.Range(0, 1000).Select(i => $"item{i:D4}").ToArray();
        var model = new CompletionModel(completions);
        model.UpdateMatchingValues("item00");

        var changes = new List<NotifyCollectionChangedEventArgs>();
        model.MatchingValues.CollectionChanged += (_, e) => changes.Add(e);

        model.UpdateMatchingValues("item01");

        Assert.AreEqual(1, changes.Count, "one update must raise one notification, not one per value");
        Assert.AreEqual(NotifyCollectionChangedAction.Reset, changes[0].Action);
        Assert.AreEqual(100, model.MatchingValues.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CompletionModelUpdateMatchingValuesReplacesPreviousResult()
    {
        var model = new CompletionModel(new[] { "apple", "banana" });
        model.UpdateMatchingValues("apple");
        model.UpdateMatchingValues("banana");

        Assert.AreEqual(1, model.MatchingValues.Count);
        Assert.AreEqual("banana", model.MatchingValues[0]);
    }
}
