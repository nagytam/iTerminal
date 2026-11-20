using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class TerminalProgressSequencesTest
{
    private static Func<string, string?> Env(params (string Name, string Value)[] variables) =>
        name => variables.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    [Timeout(60_000)]
    [TestMethod]
    public void Progress_Normal_IsStateOneWithThePercentage()
    {
        Assert.AreEqual("\u001b]9;4;1;40\u0007", TerminalProgressSequences.Progress(TerminalProgressState.Normal, 40));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Progress_Paused_IsStateFourWithThePercentage()
    {
        Assert.AreEqual("\u001b]9;4;4;75\u0007", TerminalProgressSequences.Progress(TerminalProgressState.Paused, 75));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Progress_Indeterminate_IsStateThreeWithoutAPercentage()
    {
        Assert.AreEqual("\u001b]9;4;3\u0007", TerminalProgressSequences.Progress(TerminalProgressState.Indeterminate, 50));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Progress_Remove_IsStateZeroWithoutAPercentage()
    {
        Assert.AreEqual("\u001b]9;4;0\u0007", TerminalProgressSequences.Progress(TerminalProgressState.Remove, 50));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Progress_ClampsThePercentageToZeroAndOneHundred_BeforeItBecomesAnInt()
    {
        Assert.AreEqual("\u001b]9;4;1;0\u0007", TerminalProgressSequences.Progress(TerminalProgressState.Normal, -5));
        Assert.AreEqual("\u001b]9;4;1;100\u0007", TerminalProgressSequences.Progress(TerminalProgressState.Normal, 250));
        // A huge long must not overflow when it is narrowed.
        Assert.AreEqual("\u001b]9;4;1;100\u0007", TerminalProgressSequences.Progress(TerminalProgressState.Normal, long.MaxValue));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SupportsProgress_WindowsTerminal_IsDetectedByWtSession()
    {
        Assert.IsTrue(TerminalProgressSequences.SupportsProgress(Env(("WT_SESSION", "abc"))));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SupportsProgress_OtherTerminal_IsNotDetected()
    {
        Assert.IsFalse(TerminalProgressSequences.SupportsProgress(Env(("TERM_PROGRAM", "iTerm.app"))));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SupportsProgress_ForcedOn_IsTrueWithoutWindowsTerminal()
    {
        Assert.IsTrue(TerminalProgressSequences.SupportsProgress(Env(("ITERMINAL_PROGRESS", "on"))));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SupportsProgress_ForcedOff_IsFalseEvenInWindowsTerminal()
    {
        Assert.IsFalse(TerminalProgressSequences.SupportsProgress(Env(("WT_SESSION", "abc"), ("ITERMINAL_PROGRESS", "off"))));
    }
}
