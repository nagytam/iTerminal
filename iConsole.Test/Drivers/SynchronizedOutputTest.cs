using iConsole.Drivers;

namespace iConsole.Test.Drivers;

[TestClass]
[DoNotParallelize]
public class SynchronizedOutputTest
{
    [TestMethod]
    [Timeout(5000)]
    public void EndFrame_WithoutBeginFrame_WritesNothing()
    {
        TextWriter original = Console.Out;
        StringWriter captured = new();
        try
        {
            Console.SetOut(captured);
            new VirtualSequenceConsoleDriver().EndFrame();
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.AreEqual(string.Empty, captured.ToString());
    }

    [TestMethod]
    [Timeout(5000)]
    public void BeginFrame_WhenDisabled_WritesNothing()
    {
        bool before = SynchronizedOutput.IsEnabledByEnvironment;
        TextWriter original = Console.Out;
        StringWriter captured = new();
        try
        {
            SynchronizedOutput.IsEnabledByEnvironment = false;
            Console.SetOut(captured);
            VirtualSequenceConsoleDriver driver = new();
            driver.BeginFrame();
            driver.EndFrame();
        }
        finally
        {
            Console.SetOut(original);
            SynchronizedOutput.IsEnabledByEnvironment = before;
        }

        Assert.AreEqual(string.Empty, captured.ToString());
    }
}
