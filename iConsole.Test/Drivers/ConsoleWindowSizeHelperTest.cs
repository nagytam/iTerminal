using iConsole.Data;
using iConsole.Drivers;

namespace iConsole.Test.Drivers;

[TestClass]
public class ConsoleWindowSizeHelperTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Normalize_WhenSizeHasNoPositiveDimension_ReturnsOriginalSize()
    {
        Size size = new(0, 5);

        Size normalized = ConsoleWindowSizeHelper.Normalize(size);

        Assert.AreEqual(size, normalized);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetBufferPosition_AddsViewportOrigin()
    {
        Position position = ConsoleWindowSizeHelper.GetBufferPosition(new Position(2, 3), new Position(4, 5));

        Assert.AreEqual(new Position(6, 8), position);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetVisiblePosition_SubtractsViewportOriginAndClampsToZero()
    {
        Position position = ConsoleWindowSizeHelper.GetVisiblePosition(new Position(2, 3), new Position(4, 10));

        Assert.AreEqual(new Position(0, 0), position);
    }
}
