using iConsole.Tabular;

namespace iConsole.Test.Tabular;

[TestClass]
public class GridQueryCompletionModelTest
{
    private static GridQueryCompletionModel Model()
    {
        var model = new GridQueryCompletionModel();
        model.SetColumns(["name", "amount", "city", "unit price"]);
        return model;
    }

    [TestMethod]
    public void CompletesTheLastWordAndKeepsTheRestOfTheLine()
    {
        GridQueryCompletionModel model = Model();

        model.UpdateMatchingValues("SELECT name WHERE am");

        CollectionAssert.AreEqual(new[] { "SELECT name WHERE amount" }, model.MatchingValues.ToArray());
    }

    [TestMethod]
    public void OffersKeywordsToo()
    {
        GridQueryCompletionModel model = Model();

        model.UpdateMatchingValues("SELECT * FROM file ORD");

        CollectionAssert.AreEqual(new[] { "SELECT * FROM file ORDER" }, model.MatchingValues.ToArray());
    }

    [TestMethod]
    public void IsCaseInsensitive()
    {
        GridQueryCompletionModel model = Model();

        model.UpdateMatchingValues("select CI");

        CollectionAssert.AreEqual(new[] { "select city" }, model.MatchingValues.ToArray());
    }

    [TestMethod]
    public void OffersNothingInsideAQuotedValueOrAfterASpace()
    {
        GridQueryCompletionModel model = Model();

        model.UpdateMatchingValues("WHERE city = 'am");
        Assert.AreEqual(0, model.MatchingValues.Count, "inside a quoted value");

        model.UpdateMatchingValues("WHERE city ");
        Assert.AreEqual(0, model.MatchingValues.Count, "no word is being typed");

        model.UpdateMatchingValues("WHERE amount > 1");
        Assert.AreEqual(0, model.MatchingValues.Count, "a number is not completed");
    }

    [TestMethod]
    public void ANameWithASpaceIsNotOffered()
    {
        GridQueryCompletionModel model = Model();

        model.UpdateMatchingValues("SELECT un");

        Assert.IsFalse(model.MatchingValues.Any(value => value.Contains("unit price")));
    }

    [TestMethod]
    public void AWordThatIsAlreadyCompleteHasNoSuggestion()
    {
        GridQueryCompletionModel model = Model();

        model.UpdateMatchingValues("SELECT name");

        Assert.AreEqual(0, model.MatchingValues.Count);
    }
}
