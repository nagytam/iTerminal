using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class ObservableValueTest
{
    private class TestObservable : System.ComponentModel.INotifyPropertyChanged
    {
        private string _value = "initial";

        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Value)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    [TestMethod]
    public void ObservableValueConstructor()
    {
        var observable = new TestObservable();
        var observableValue = new ObservableValue<TestObservable>(observable, obj => obj.Value);
        Assert.AreEqual("initial", observableValue.Value);
    }

    [TestMethod]
    public void ObservableValueReturnsExtractedValue()
    {
        var observable = new TestObservable { Value = "test" };
        var observableValue = new ObservableValue<TestObservable>(observable, obj => obj.Value);
        Assert.AreEqual("test", observableValue.Value);
    }

    [TestMethod]
    public void ObservableValueUpdatesWhenSourceChanges()
    {
        var observable = new TestObservable { Value = "initial" };
        var observableValue = new ObservableValue<TestObservable>(observable, obj => obj.Value);

        observable.Value = "updated";
        Assert.AreEqual("updated", observableValue.Value);
    }

    [TestMethod]
    public void ObservableValueRaisesPropertyChanged()
    {
        var observable = new TestObservable { Value = "initial" };
        var observableValue = new ObservableValue<TestObservable>(observable, obj => obj.Value);
        bool propertyChanged = false;

        observableValue.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ObservableValue<TestObservable>.Value))
                propertyChanged = true;
        };

        observable.Value = "updated";
        Assert.IsTrue(propertyChanged);
    }

    [TestMethod]
    public void ObservableValueWithCustomExtractor()
    {
        var observable = new TestObservable { Value = "test" };
        var observableValue = new ObservableValue<TestObservable>(observable, obj => obj.Value.ToUpper());
        Assert.AreEqual("TEST", observableValue.Value);
    }
}
