using iConsole.Data;
using iConsole.Model;

namespace iConsole.Test.Model;

[TestClass]
public class ScrollableModelTest
{
    [TestMethod]
    public void Constructor_InitializesProperties()
    {
        var model = new ScrollableModel();

        Assert.IsNotNull(model.CursorPosition);
        Assert.IsNotNull(model.ScrollPosition);
        Assert.IsNotNull(model.ViewSize);
        Assert.IsNotNull(model.ScrollableSize);
    }

    [TestMethod]
    public void CursorPosition_DefaultValue_IsZero()
    {
        var model = new ScrollableModel();

        Assert.AreEqual(0, model.CursorPosition.X);
        Assert.AreEqual(0, model.CursorPosition.Y);
    }

    [TestMethod]
    public void CursorPosition_SetValue_UpdatesProperty()
    {
        var model = new ScrollableModel();
        var position = new Position(10, 20);

        model.CursorPosition = position;

        Assert.AreEqual(10, model.CursorPosition.X);
        Assert.AreEqual(20, model.CursorPosition.Y);
    }

    [TestMethod]
    public void CursorPosition_SetValue_RaisesPropertyChanged()
    {
        var model = new ScrollableModel();
        var propertyChangedRaised = false;
        model.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(model.CursorPosition)) propertyChangedRaised = true; };

        model.CursorPosition = new Position(5, 5);

        Assert.IsTrue(propertyChangedRaised);
    }

    [TestMethod]
    public void ScrollPosition_DefaultValue_IsZero()
    {
        var model = new ScrollableModel();

        Assert.AreEqual(0, model.ScrollPosition.X);
        Assert.AreEqual(0, model.ScrollPosition.Y);
    }

    [TestMethod]
    public void ScrollPosition_SetValue_UpdatesProperty()
    {
        var model = new ScrollableModel();
        var position = new Position(15, 25);

        model.ScrollPosition = position;

        Assert.AreEqual(15, model.ScrollPosition.X);
        Assert.AreEqual(25, model.ScrollPosition.Y);
    }

    [TestMethod]
    public void ScrollPosition_SetValue_RaisesPropertyChanged()
    {
        var model = new ScrollableModel();
        var propertyChangedRaised = false;
        model.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(model.ScrollPosition)) propertyChangedRaised = true; };

        model.ScrollPosition = new Position(3, 7);

        Assert.IsTrue(propertyChangedRaised);
    }

    [TestMethod]
    public void ViewSize_DefaultValue_IsZero()
    {
        var model = new ScrollableModel();

        Assert.AreEqual(0, model.ViewSize.Width);
        Assert.AreEqual(0, model.ViewSize.Height);
    }

    [TestMethod]
    public void ViewSize_SetValue_UpdatesProperty()
    {
        var model = new ScrollableModel();
        var size = new Size(80, 24);

        model.ViewSize = size;

        Assert.AreEqual(80, model.ViewSize.Width);
        Assert.AreEqual(24, model.ViewSize.Height);
    }

    [TestMethod]
    public void ViewSize_SetValue_RaisesPropertyChanged()
    {
        var model = new ScrollableModel();
        var propertyChangedRaised = false;
        model.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(model.ViewSize)) propertyChangedRaised = true; };

        model.ViewSize = new Size(100, 50);

        Assert.IsTrue(propertyChangedRaised);
    }

    [TestMethod]
    public void ScrollableSize_DefaultValue_IsZero()
    {
        var model = new ScrollableModel();

        Assert.AreEqual(0, model.ScrollableSize.Width);
        Assert.AreEqual(0, model.ScrollableSize.Height);
    }

    [TestMethod]
    public void ScrollableSize_SetValue_UpdatesProperty()
    {
        var model = new ScrollableModel();
        var size = new Size(200, 100);

        model.ScrollableSize = size;

        Assert.AreEqual(200, model.ScrollableSize.Width);
        Assert.AreEqual(100, model.ScrollableSize.Height);
    }

    [TestMethod]
    public void ScrollableSize_SetValue_RaisesPropertyChanged()
    {
        var model = new ScrollableModel();
        var propertyChangedRaised = false;
        model.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(model.ScrollableSize)) propertyChangedRaised = true; };

        model.ScrollableSize = new Size(150, 75);

        Assert.IsTrue(propertyChangedRaised);
    }

    [TestMethod]
    public void SetSameValue_DoesNotRaisePropertyChanged()
    {
        var model = new ScrollableModel();
        var position = new Position(10, 10);
        model.CursorPosition = position;
        var propertyChangedCount = 0;
        model.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(model.CursorPosition)) propertyChangedCount++; };

        model.CursorPosition = position;

        Assert.AreEqual(0, propertyChangedCount);
    }
}
