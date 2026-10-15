using iConsole.Inputs;

namespace iConsole.Test.Inputs;

[TestClass]
public class TerminalMouseAnsiTest
{
    [TestMethod]
    public void EnablePreferredMouseReporting_CombinesCellMotionAndSgrSequences()
    {
        Assert.AreEqual(
            TerminalMouseAnsi.EnableCellMotionMouse + TerminalMouseAnsi.EnableSgrExtendedMouse,
            TerminalMouseAnsi.EnablePreferredMouseReporting);
    }

    [TestMethod]
    public void DisablePreferredMouseReporting_CombinesDisableSequences()
    {
        Assert.AreEqual(
            TerminalMouseAnsi.DisableSgrExtendedMouse + TerminalMouseAnsi.DisableCellMotionMouse + TerminalMouseAnsi.DisableBasicMouse,
            TerminalMouseAnsi.DisablePreferredMouseReporting);
    }
}
