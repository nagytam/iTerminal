using iConsole.Inputs;

namespace iConsole.Test.Inputs;

[TestClass]
public class TerminalMouseAnsiTest
{
    [TestMethod]
    public void EnablePreferredMouseReporting_CombinesAnyMotionAndSgrSequences()
    {
        Assert.AreEqual(
            TerminalMouseAnsi.EnableAnyMotionMouse + TerminalMouseAnsi.EnableSgrExtendedMouse,
            TerminalMouseAnsi.EnablePreferredMouseReporting);
    }

    [TestMethod]
    public void DisablePreferredMouseReporting_CombinesDisableSequences()
    {
        Assert.AreEqual(
            TerminalMouseAnsi.DisableSgrExtendedMouse + TerminalMouseAnsi.DisableAnyMotionMouse + TerminalMouseAnsi.DisableCellMotionMouse + TerminalMouseAnsi.DisableBasicMouse,
            TerminalMouseAnsi.DisablePreferredMouseReporting);
    }
}
