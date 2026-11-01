using iTerminal.Core.Controls.Terminals;

namespace iTerminal.Core.Test.Controls.Terminals;

[TestClass]
public class PromptLineTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void AShortLineIsPaddedToTheWidth()
    {
        Assert.AreEqual("Terminal>dir     ", PromptLine.Format("Terminal>", "dir", 17));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ALongInputKeepsThePrefixAndTheEndBeingTyped()
    {
        var line = PromptLine.Format("Terminal>", "git commit -m message", 20);

        Assert.AreEqual(20, line.Length);
        Assert.IsTrue(line.StartsWith("Terminal>…", StringComparison.Ordinal), line);
        Assert.IsTrue(line.EndsWith("-m message", StringComparison.Ordinal), line);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnInputThatExactlyFitsIsNotElided()
    {
        Assert.AreEqual("> abcde", PromptLine.Format("> ", "abcde", 7));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WhenThePrefixDoesNotFitTheInputStillWins()
    {
        var line = PromptLine.Format("A very long prompt>", "xyz", 5);

        Assert.AreEqual(5, line.Length);
        Assert.IsTrue(line.TrimEnd().EndsWith("xyz", StringComparison.Ordinal), line);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANonPositiveWidthIsEmpty()
    {
        Assert.AreEqual(string.Empty, PromptLine.Format("x", "y", 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NullsAreTreatedAsEmpty()
    {
        Assert.AreEqual("   ", PromptLine.Format(null!, null!, 3));
    }
}
