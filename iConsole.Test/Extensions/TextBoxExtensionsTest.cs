using iConsole.Controls;
using iConsole.Extensions;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Extensions;

[TestClass]
public class TextBoxExtensionsTest
{
    private static TextBox CreateTextBox()
    {
        var beep = Mock.Of<IBeepService>();
        var dt = Mock.Of<IDateTimeProviderService>();
        var theme = Mock.Of<IThemeService>();
        return new TextBox(beep, dt, theme)
        {
            ViewSize = new iConsole.Data.Size(40, 1)
        };
    }

    [TestMethod]
    public void WithAutoCompletion_IEnumerable_ReturnsTextBox()
    {
        var textBox = CreateTextBox();
        var completions = new[] { "alpha", "beta", "gamma" };

        var result = textBox.WithAutoCompletion(completions);

        Assert.IsNotNull(result);
        Assert.AreSame(textBox, result);
    }

    [TestMethod]
    public void WithAutoCompletion_IEnumerable_AttachesAutoCompletion()
    {
        var textBox = CreateTextBox();
        var completions = new[] { "alpha", "beta" };

        textBox.WithAutoCompletion(completions);

        Assert.IsNotNull(textBox.AutoCompletion);
    }

    [TestMethod]
    public void WithAutoCompletion_IEnumerable_AutoCompletionIsVisible_WhenItemsExist()
    {
        var textBox = CreateTextBox();
        var completions = new[] { "alpha" };

        textBox.WithAutoCompletion(completions);

        Assert.IsTrue(textBox.AutoCompletion!.IsVisible);
    }

    [TestMethod]
    public void WithAutoCompletion_IEnumerable_AutoCompletionIsNotVisible_WhenEmpty()
    {
        var textBox = CreateTextBox();
        var completions = Enumerable.Empty<string>();

        textBox.WithAutoCompletion(completions);

        Assert.IsFalse(textBox.AutoCompletion!.IsVisible);
    }

    [TestMethod]
    public void WithAutoCompletion_ICompletionModel_ReturnsTextBox()
    {
        var textBox = CreateTextBox();
        var model = new CompletionModel(["one", "two"]);

        var result = textBox.WithAutoCompletion(model);

        Assert.IsNotNull(result);
        Assert.AreSame(textBox, result);
    }

    [TestMethod]
    public void WithAutoCompletion_ICompletionModel_AttachesAutoCompletion()
    {
        var textBox = CreateTextBox();
        var model = new CompletionModel(["one", "two"]);

        textBox.WithAutoCompletion(model);

        Assert.IsNotNull(textBox.AutoCompletion);
    }

    [TestMethod]
    public void WithAutoCompletion_ICompletionModel_IsVisible_WhenCompletionsExist()
    {
        var textBox = CreateTextBox();
        var model = new CompletionModel(["one"]);

        textBox.WithAutoCompletion(model);

        Assert.IsTrue(textBox.AutoCompletion!.IsVisible);
    }

    [TestMethod]
    public void WithAutoCompletion_ICompletionModel_IsNotVisible_WhenEmpty()
    {
        var textBox = CreateTextBox();
        var model = new CompletionModel([]);

        textBox.WithAutoCompletion(model);

        Assert.IsFalse(textBox.AutoCompletion!.IsVisible);
    }
}
