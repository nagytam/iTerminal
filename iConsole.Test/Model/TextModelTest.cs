using iConsole.Model;

namespace iConsole.Test.Model;

[TestClass]
public class TextModelTest
{
    [TestMethod]
    public void TextModelConstructorEmpty()
    {
        var model = new TextModel();
        Assert.IsNotNull(model.Lines);
        Assert.AreEqual(0, model.Lines.Count);
    }

    [TestMethod]
    public void TextModelConstructorWithText()
    {
        var model = new TextModel("Hello");
        Assert.AreEqual(1, model.Lines.Count);
        Assert.AreEqual("Hello", model.Lines[0]);
    }

    [TestMethod]
    public void TextModelDefaultEncoding()
    {
        var model = new TextModel();
        Assert.AreEqual(System.Text.Encoding.UTF8, model.Encoding);
    }

    [TestMethod]
    public void TextModelSetEncoding()
    {
        var model = new TextModel();
        model.Encoding = System.Text.Encoding.ASCII;
        Assert.AreEqual(System.Text.Encoding.ASCII, model.Encoding);
    }

    [TestMethod]
    public void TextModelAddLine()
    {
        var model = new TextModel();
        model.Lines.Add("Line 1");
        Assert.AreEqual(1, model.Lines.Count);
        Assert.AreEqual("Line 1", model.Lines[0]);
    }

    [TestMethod]
    public void TextModelMultipleLines()
    {
        var model = new TextModel();
        model.Lines.Add("Line 1");
        model.Lines.Add("Line 2");
        model.Lines.Add("Line 3");
        Assert.AreEqual(3, model.Lines.Count);
    }

    [TestMethod]
    public void TextModelTriggerPropertyChanged()
    {
        var model = new TextModel();
        bool propertyChanged = false;

        model.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TextModel.Lines))
                propertyChanged = true;
        };

        model.TriggerPropertyChanged();
        Assert.IsTrue(propertyChanged);
    }

    [TestMethod]
    public void TextModelInterface()
    {
        var model = new TextModel();
        Assert.IsInstanceOfType(model, typeof(ITextModel));
        Assert.IsInstanceOfType(model, typeof(System.ComponentModel.INotifyPropertyChanged));
    }
}
