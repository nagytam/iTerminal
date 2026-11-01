using iConsole.Display;
using iConsole.Drivers;

namespace iConsole.Test.Drivers;

[TestClass]
[DoNotParallelize]
public class CursorShapeDriverTest
{
    private static string Capture(Action<VirtualSequenceConsoleDriver> act, bool enabled = true)
    {
        bool before = CursorShapes.IsEnabledByEnvironment;
        TextWriter original = Console.Out;
        StringWriter captured = new();
        try
        {
            CursorShapes.IsEnabledByEnvironment = enabled;
            Console.SetOut(captured);
            act(new VirtualSequenceConsoleDriver());
        }
        finally
        {
            Console.SetOut(original);
            CursorShapes.IsEnabledByEnvironment = before;
        }

        return captured.ToString();
    }

    [TestMethod]
    [Timeout(5000)]
    public void SettingTheSameShapeTwice_WritesAtMostOneSequence()
    {
        string output = Capture(driver =>
        {
            driver.CursorShape = CursorShape.BlinkingBar;
            driver.CursorShape = CursorShape.BlinkingBar;
        });

        Assert.IsLessThanOrEqualTo(1, output.Split("\u001b[5 q").Length - 1);
    }

    [TestMethod]
    [Timeout(5000)]
    public void TheDriverRemembersTheShape()
    {
        CursorShape shape = CursorShape.Default;
        Capture(driver =>
        {
            driver.CursorShape = CursorShape.SteadyUnderline;
            shape = driver.CursorShape;
        });

        Assert.AreEqual(CursorShape.SteadyUnderline, shape);
    }

    [TestMethod]
    [Timeout(5000)]
    public void WhenDisabled_NoSequenceIsWritten()
    {
        string output = Capture(driver => driver.CursorShape = CursorShape.BlinkingBar, enabled: false);

        Assert.AreEqual(string.Empty, output);
    }

    [TestMethod]
    [Timeout(5000)]
    public void GivingTheTerminalBack_ResetsTheShape()
    {
        CursorShape after = CursorShape.BlinkingBar;
        Capture(driver =>
        {
            driver.CursorShape = CursorShape.BlinkingBar;
            driver.OnConsoleReleased();
            after = driver.CursorShape;
        });

        Assert.AreEqual(CursorShape.Default, after);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDefaultDriverMemberDoesNothing()
    {
        IConsoleDriver driver = new Moq.Mock<IConsoleDriver> { CallBase = true }.Object;

        driver.CursorShape = CursorShape.BlinkingBar;

        Assert.AreEqual(CursorShape.Default, driver.CursorShape);
    }
}
