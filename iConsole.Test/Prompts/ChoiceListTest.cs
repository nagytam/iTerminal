using iConsole.Prompts;

namespace iConsole.Test.Prompts;

[TestClass]
public class ChoiceListTest
{
    private static ChoiceList<string> Fruit() =>
        new(["Apple", "Banana", "Blackberry", "Cherry"], item => item);

    [Timeout(60_000)]
    [TestMethod]
    public void Move_StopsAtTheFirstAndLastChoice()
    {
        var list = Fruit();

        list.Move(-1);
        Assert.AreEqual("Apple", list.Current);

        list.Move(10);
        Assert.AreEqual("Cherry", list.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Filter_ShowsOnlyTheChoicesThatContainTheText_IgnoringCase()
    {
        var list = Fruit();

        list.Type('B');

        Assert.AreEqual(2, list.VisibleCount, "Banana and Blackberry both contain 'b', ignoring case.");
        Assert.AreEqual("Banana", list.LabelAt(0));
        Assert.AreEqual("Blackberry", list.LabelAt(1));

        list.Type('l');

        Assert.AreEqual(1, list.VisibleCount);
        Assert.AreEqual("Blackberry", list.Current);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Backspace_WidensTheFilterAgain()
    {
        var list = Fruit();
        list.Type('z');
        Assert.AreEqual(0, list.VisibleCount);
        Assert.IsNull(list.Current);

        list.Backspace();

        Assert.AreEqual(4, list.VisibleCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Marks_SurviveTheFilterChanging()
    {
        var list = Fruit();
        list.ToggleCurrent();          // Apple
        list.Type('c');                // Blackberry and Cherry are visible
        list.Move(1);                  // Cherry
        list.ToggleCurrent();          // Cherry
        list.Backspace();              // everything again

        CollectionAssert.AreEqual(new[] { "Apple", "Cherry" }, list.MarkedItems.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleTwice_UnmarksTheChoice()
    {
        var list = Fruit();
        list.ToggleCurrent();
        list.ToggleCurrent();

        Assert.AreEqual(0, list.MarkedItems.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollTop_MovesOnlyWhenTheHighlightLeavesTheWindow()
    {
        var list = new ChoiceList<int>(Enumerable.Range(0, 20).ToList(), item => item.ToString());

        Assert.AreEqual(0, list.ScrollTop(5));
        list.Move(4);
        Assert.AreEqual(0, list.ScrollTop(5), "The highlight is still in the window, so the list does not move.");
        list.Move(1);
        Assert.AreEqual(1, list.ScrollTop(5), "One step past the window scrolls it by one.");
    }
}
