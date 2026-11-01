using iConsole.Data;
using iConsole.Model;

namespace iConsole.Test.Model;

[TestClass]
public class SelectionTextModelTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesProperties()
    {
        var model = new SelectionTextModel();

        Assert.IsNull(model.Start);
        Assert.IsNull(model.End);
        Assert.IsFalse(model.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Start_DefaultValue_IsNull()
    {
        var model = new SelectionTextModel();

        Assert.IsNull(model.Start);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Start_SetValue_UpdatesProperty()
    {
        var model = new SelectionTextModel();
        var position = new Position(10, 5);

        model.Start = position;

        Assert.IsNotNull(model.Start);
        Assert.AreEqual(10, model.Start.X);
        Assert.AreEqual(5, model.Start.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void End_DefaultValue_IsNull()
    {
        var model = new SelectionTextModel();

        Assert.IsNull(model.End);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void End_SetValue_UpdatesProperty()
    {
        var model = new SelectionTextModel();
        var position = new Position(20, 15);

        model.End = position;

        Assert.IsNotNull(model.End);
        Assert.AreEqual(20, model.End.X);
        Assert.AreEqual(15, model.End.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsSelected_DefaultValue_IsFalse()
    {
        var model = new SelectionTextModel();

        Assert.IsFalse(model.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsSelected_SetValue_UpdatesProperty()
    {
        var model = new SelectionTextModel();

        model.IsSelected = true;

        Assert.IsTrue(model.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanSetAllProperties()
    {
        var model = new SelectionTextModel
        {
            Start = new Position(5, 10),
            End = new Position(15, 20),
            IsSelected = true
        };

        Assert.IsNotNull(model.Start);
        Assert.AreEqual(5, model.Start.X);
        Assert.AreEqual(10, model.Start.Y);
        Assert.IsNotNull(model.End);
        Assert.AreEqual(15, model.End.X);
        Assert.AreEqual(20, model.End.Y);
        Assert.IsTrue(model.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanClearSelection()
    {
        var model = new SelectionTextModel
        {
            Start = new Position(5, 10),
            End = new Position(15, 20),
            IsSelected = true
        };

        model.Start = null;
        model.End = null;
        model.IsSelected = false;

        Assert.IsNull(model.Start);
        Assert.IsNull(model.End);
        Assert.IsFalse(model.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SelectionWithSameStartEnd_IsValid()
    {
        var model = new SelectionTextModel
        {
            Start = new Position(10, 10),
            End = new Position(10, 10),
            IsSelected = true
        };

        Assert.AreEqual(model.Start.X, model.End.X);
        Assert.AreEqual(model.Start.Y, model.End.Y);
        Assert.IsTrue(model.IsSelected);
    }
}
