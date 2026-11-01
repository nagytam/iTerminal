using iConsole.Data;
using iConsole.Helper;

namespace iConsole.Test.Helper;

/// <summary>
/// Not parallelised: <see cref="KeyTraceHelper.SetEnabledForTest"/> flips a process-wide switch, and
/// every test that feeds keys through a consumer job calls <see cref="KeyTraceHelper.Trace"/>. Run
/// alongside them, one test forcing tracing on made the others append to the very file
/// <see cref="Trace_WhenDisabled_WritesNothing"/> measures.
/// </summary>
[TestClass]
[DoNotParallelize]
public class KeyTraceHelperTest
{
    [TestCleanup]
    public void Cleanup() => KeyTraceHelper.SetEnabledForTest(null);

    [Timeout(60_000)]
    [TestMethod]
    public void Format_SpellsOutTheWholeChord()
    {
        var line = KeyTraceHelper.Format(new KeyEvent('\r', ConsoleKey.M, shift: true, alt: false, control: true), isHandled: true);

        StringAssert.Contains(line, "key=M");
        StringAssert.Contains(line, "Ctrl+Shift+M");
        StringAssert.Contains(line, "char=0x0d");
        StringAssert.Contains(line, "handled=True");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Format_MarksUnhandledKeys()
    {
        var line = KeyTraceHelper.Format(new KeyEvent('m', ConsoleKey.M, false, true, false), isHandled: false);

        StringAssert.Contains(line, "Alt+M");
        StringAssert.Contains(line, "handled=False");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Trace_WhenDisabled_WritesNothing()
    {
        KeyTraceHelper.SetEnabledForTest(false);
        var before = File.Exists(KeyTraceHelper.TraceFilePath)
            ? new FileInfo(KeyTraceHelper.TraceFilePath).Length
            : 0;

        KeyTraceHelper.Trace(new KeyEvent('x', ConsoleKey.X, false, false, false), isHandled: false);

        var after = File.Exists(KeyTraceHelper.TraceFilePath)
            ? new FileInfo(KeyTraceHelper.TraceFilePath).Length
            : 0;
        Assert.AreEqual(before, after, "Tracing must be inert unless explicitly enabled");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Trace_WithANullKeyEvent_DoesNotThrow()
    {
        KeyTraceHelper.SetEnabledForTest(true);

        KeyTraceHelper.Trace(null, isHandled: false);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsEnabled_DefaultsToOff()
    {
        KeyTraceHelper.SetEnabledForTest(null);

        // The environment variable is not set in the test run, so tracing must stay off.
        Assert.IsFalse(KeyTraceHelper.IsEnabled);
    }
}
