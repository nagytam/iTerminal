using iConsole.Prompts;

namespace iConsole.Test.Prompts;

[TestClass]
public class TextPromptTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void StringPrompt_TakesTheTextAsTyped()
    {
        var prompt = new TextPrompt<string>("Name?");

        Assert.IsTrue(prompt.TryParse("Ada", out var value, out var error));
        Assert.AreEqual("Ada", value);
        Assert.IsNull(error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StringPrompt_RefusesAnEmptyAnswerUnlessAllowed()
    {
        var prompt = new TextPrompt<string>("Name?");

        Assert.IsFalse(prompt.TryParse(string.Empty, out _, out var error));
        Assert.AreEqual("Enter a value.", error);

        prompt.AllowEmpty = true;
        Assert.IsTrue(prompt.TryParse(string.Empty, out _, out _));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NumberPrompt_ConvertsWithTheInvariantConverter()
    {
        var prompt = new TextPrompt<int>("Age?");

        Assert.IsTrue(prompt.TryParse(" 42 ", out var value, out _));
        Assert.AreEqual(42, value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NumberPrompt_ReportsWhatWasNotANumber()
    {
        var prompt = new TextPrompt<int>("Age?");

        Assert.IsFalse(prompt.TryParse("forty", out _, out var error));
        Assert.AreEqual("'forty' is not a valid Int32.", error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Validate_RulesRunAfterTheConversion_AndTheFirstFailureWins()
    {
        var prompt = new TextPrompt<int>("Age?")
            .Validate(age => age < 0 ? "Not below zero." : null)
            .Validate(age => age > 120 ? "Too old." : null);

        Assert.IsFalse(prompt.TryParse("-1", out _, out var negative));
        Assert.AreEqual("Not below zero.", negative);

        Assert.IsFalse(prompt.TryParse("200", out _, out var tooOld));
        Assert.AreEqual("Too old.", tooOld);

        Assert.IsTrue(prompt.TryParse("30", out var value, out _));
        Assert.AreEqual(30, value);
    }
}
