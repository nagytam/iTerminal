using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class LineEndingsDetectTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("a\r\nb\r\nc\n", "\r\n")]
    [DataRow("a\nb\nc\r\n", "\n")]
    [DataRow("a\rb\rc\r", "\r")]
    [DataRow("a\r\r\nb\r", "\r")]
    [DataRow("a\r", "\r")]
    [DataRow("\n", "\n")]
    public void Detect_ReturnsTheMostCommonBreak(string text, string expected)
    {
        Assert.AreEqual(expected, LineEndings.Detect(text));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Detect_NoBreak_ReturnsTheSystemBreak()
    {
        Assert.AreEqual(Environment.NewLine, LineEndings.Detect("no break"));
        Assert.AreEqual(Environment.NewLine, LineEndings.Detect(null));
    }
}
