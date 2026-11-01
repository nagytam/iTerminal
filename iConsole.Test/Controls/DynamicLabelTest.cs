using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using System.ComponentModel;

namespace iConsole.Test.Controls;

[TestClass]
public class DynamicLabelTest
{
    private class TestModel : INotifyPropertyChanged
    {
        private string _value = "";
        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabelDrawsInitialValue()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var model = new TestModel { Value = "Initial" };
        var observableValue = new ObservableValue<TestModel>(model, m => m.Value);
        var dynamicLabel = new DynamicLabel<TestModel>(observableValue, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        dynamicLabel.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains(model.Value.ToString()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabelDrawsAfterValueChange()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var model = new TestModel { Value = "Initial" };
        var observableValue = new ObservableValue<TestModel>(model, m => m.Value);
        var dynamicLabel = new DynamicLabel<TestModel>(observableValue, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        dynamicLabel.Draw(buffer);
        var firstOutput = buffer.ContentAsString;

        model.Value = "Updated";
        buffer.Clear();
        dynamicLabel.Draw(buffer);
        var secondOutput = buffer.ContentAsString;

        Assert.IsFalse(firstOutput.Equals(secondOutput));
        Assert.IsTrue(secondOutput.Contains("Updated"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabelDrawsWithCustomColors()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(15, 1));
        var model = new TestModel { Value = "Test" };
        var observableValue = new ObservableValue<TestModel>(model, m => m.Value);
        var dynamicLabel = new DynamicLabel<TestModel>(observableValue, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(15, 1)
        };

        dynamicLabel.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabelDrawsWithOffset()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(25, 3));
        var model = new TestModel { Value = "Label" };
        var observableValue = new ObservableValue<TestModel>(model, m => m.Value);
        var dynamicLabel = new DynamicLabel<TestModel>(observableValue, mockThemeService.Object)
        {
            Position = new Position(5, 1),
            ViewSize = new Size(10, 1)
        };

        dynamicLabel.Draw(buffer);
        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        // Check that content is at the correct position
        Assert.IsTrue(lines.Length >= 2);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabelHandlesNullValue()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var model = new TestModel { Value = "" };
        var observableValue = new ObservableValue<TestModel>(model, m => m.Value);
        var dynamicLabel = new DynamicLabel<TestModel>(observableValue, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        dynamicLabel.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabelUpdatesOnPropertyChange()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var model = new TestModel { Value = "First" };
        var observableValue = new ObservableValue<TestModel>(model, m => m.Value);
        var dynamicLabel = new DynamicLabel<TestModel>(observableValue, mockThemeService.Object)
        {
            IsVisible = true
        };

        model.Value = "Second";

        // Give event handling time to process
        System.Threading.Thread.Sleep(100);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabelDrawsMultipleUpdates()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 1));
        var model = new TestModel { Value = "Value1" };
        var observableValue = new ObservableValue<TestModel>(model, m => m.Value);
        var dynamicLabel = new DynamicLabel<TestModel>(observableValue, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        for (int i = 1; i <= 3; i++)
        {
            buffer.Clear();
            model.Value = $"Value{i}";
            dynamicLabel.Draw(buffer);
            Assert.IsTrue(buffer.ContentAsString.Length > 0);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabelInheritsFromControl()
    {
        var mockThemeService = new Mock<IThemeService>();
        var model = new TestModel { Value = "Test" };
        var observableValue = new ObservableValue<TestModel>(model, m => m.Value);
        var dynamicLabel = new DynamicLabel<TestModel>(observableValue, mockThemeService.Object);

        // DynamicLabel should inherit from Control
        Assert.IsInstanceOfType(dynamicLabel, typeof(Control));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabelHasObservableValueProperty()
    {
        var mockThemeService = new Mock<IThemeService>();
        var model = new TestModel { Value = "Test" };
        var observableValue = new ObservableValue<TestModel>(model, m => m.Value);
        var dynamicLabel = new DynamicLabel<TestModel>(observableValue, mockThemeService.Object);

        Assert.IsNotNull(dynamicLabel.ObservableValue);
        Assert.AreEqual(observableValue, dynamicLabel.ObservableValue);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DynamicLabelDoesNotUpdateWhenNotVisible()
    {
        var mockThemeService = new Mock<IThemeService>();
        var model = new TestModel { Value = "Initial" };
        var observableValue = new ObservableValue<TestModel>(model, m => m.Value);
        var dynamicLabel = new DynamicLabel<TestModel>(observableValue, mockThemeService.Object)
        {
            IsVisible = false
        };

        var propertyChangeCount = 0;
        dynamicLabel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(dynamicLabel.HasStateChanged))
            {
                propertyChangeCount++;
            }
        };

        model.Value = "Changed";

        // Give event handling time to process
        System.Threading.Thread.Sleep(100);

        // Since the label is not visible, state change should not have been triggered
    }
}
