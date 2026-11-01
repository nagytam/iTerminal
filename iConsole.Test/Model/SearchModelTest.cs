using iConsole.Data;
using iConsole.Model;

namespace iConsole.Test.Model;

[TestClass]
public class SearchModelTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesProperties()
    {
        var model = new SearchModel();

        Assert.IsNotNull(model.Hits);
        Assert.AreEqual(string.Empty, model.Search);
        Assert.AreEqual(0, model.Hits.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Search_DefaultValue_IsEmpty()
    {
        var model = new SearchModel();

        Assert.AreEqual(string.Empty, model.Search);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Search_SetValue_UpdatesProperty()
    {
        var model = new SearchModel();

        model.Search = "test query";

        Assert.AreEqual("test query", model.Search);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Search_SetValue_RaisesPropertyChanged()
    {
        var model = new SearchModel();
        var propertyChangedRaised = false;
        model.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(model.Search)) propertyChangedRaised = true; };

        model.Search = "new search";

        Assert.IsTrue(propertyChangedRaised);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Search_SetSameValue_DoesNotRaisePropertyChanged()
    {
        var model = new SearchModel();
        model.Search = "test";
        var propertyChangedCount = 0;
        model.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(model.Search)) propertyChangedCount++; };

        model.Search = "test";

        Assert.AreEqual(0, propertyChangedCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Hits_CanAddItems()
    {
        var model = new SearchModel();
        var position = new Position(10, 5);

        model.Hits.Add(position);

        Assert.AreEqual(1, model.Hits.Count);
        Assert.AreEqual(10, model.Hits[0].X);
        Assert.AreEqual(5, model.Hits[0].Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Hits_CollectionChanged_RaisesPropertyChanged()
    {
        var model = new SearchModel();
        var propertyChangedRaised = false;
        model.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(model.Hits)) propertyChangedRaised = true; };

        model.Hits.Add(new Position(0, 0));

        Assert.IsTrue(propertyChangedRaised);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Hits_CanClearItems()
    {
        var model = new SearchModel();
        model.Hits.Add(new Position(1, 1));
        model.Hits.Add(new Position(2, 2));

        model.Hits.Clear();

        Assert.AreEqual(0, model.Hits.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Hits_CanRemoveItems()
    {
        var model = new SearchModel();
        var position = new Position(5, 5);
        model.Hits.Add(position);

        model.Hits.Remove(position);

        Assert.AreEqual(0, model.Hits.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Hits_MultipleHits_MaintainsOrder()
    {
        var model = new SearchModel();
        var pos1 = new Position(1, 1);
        var pos2 = new Position(2, 2);
        var pos3 = new Position(3, 3);

        model.Hits.Add(pos1);
        model.Hits.Add(pos2);
        model.Hits.Add(pos3);

        Assert.AreEqual(3, model.Hits.Count);
        Assert.AreEqual(1, model.Hits[0].X);
        Assert.AreEqual(2, model.Hits[1].X);
        Assert.AreEqual(3, model.Hits[2].X);
    }
}
