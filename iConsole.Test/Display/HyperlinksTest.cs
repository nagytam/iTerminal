using iConsole.Display;

namespace iConsole.Test.Display;

[TestClass]
public class HyperlinksTest
{
    [TestMethod]
    public void RegisterGivesTheSameIdToTheSameTarget()
    {
        ushort first = Hyperlinks.Register("https://example.com/a");
        ushort second = Hyperlinks.Register("https://example.com/a");

        Assert.AreNotEqual(Hyperlinks.None, first);
        Assert.AreEqual(first, second);
        Assert.AreEqual("https://example.com/a", Hyperlinks.Resolve(first));
    }

    [TestMethod]
    public void RegisterGivesDifferentTargetsDifferentIds()
    {
        Assert.AreNotEqual(Hyperlinks.Register("https://example.com/one"), Hyperlinks.Register("https://example.com/two"));
    }

    [TestMethod]
    public void EmptyAndTooLongTargetsAreNoLink()
    {
        Assert.AreEqual(Hyperlinks.None, Hyperlinks.Register(null));
        Assert.AreEqual(Hyperlinks.None, Hyperlinks.Register(string.Empty));
        Assert.AreEqual(Hyperlinks.None, Hyperlinks.Register("https://example.com/" + new string('x', Hyperlinks.MaxTargetLength)));
        Assert.IsNull(Hyperlinks.Resolve(Hyperlinks.None));
    }

    [TestMethod]
    public void ControlCharactersAndNonAsciiArePercentEncoded()
    {
        // An ESC or BEL in the target would end the OSC sequence early and let the text choose what the terminal does next.
        ushort id = Hyperlinks.Register("https://example.com/\x1b]0;pwned\a é");

        string? target = Hyperlinks.Resolve(id);

        Assert.IsNotNull(target);
        Assert.IsFalse(target.Any(c => c < 0x21 || c > 0x7E));
        Assert.IsTrue(target.Contains("%1B", StringComparison.Ordinal));
        Assert.IsTrue(target.EndsWith("%20%C3%A9", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AnIdNeverHandedOutResolvesToNothing()
    {
        Assert.IsNull(Hyperlinks.Resolve(ushort.MaxValue));
    }
}
