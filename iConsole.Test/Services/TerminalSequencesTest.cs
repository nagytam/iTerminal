using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class TerminalSequencesTest
{
    private static Func<string, string?> Env(params (string Name, string Value)[] variables) =>
        name => variables.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    [TestMethod]
    public void SetTitle_IsOsc2_EndedByBel()
    {
        Assert.AreEqual("\u001b]2;Hello\u0007", TerminalSequences.SetTitle("Hello"));
    }

    [TestMethod]
    public void SetTitle_DropsEscapeAndBel_SoTheTextCannotEndTheSequenceEarly()
    {
        Assert.AreEqual("\u001b]2;AB[31mC\u0007", TerminalSequences.SetTitle("A\u001bB\u0007[31mC"));
    }

    [TestMethod]
    public void SetTitle_TurnsLineBreaksIntoSpaces_AndKeepsSemicolons()
    {
        Assert.AreEqual("\u001b]2;a  b;c\u0007", TerminalSequences.SetTitle("a\r\nb;c"));
    }

    [TestMethod]
    public void Sanitize_CutsAtTheLimit_WithoutSplittingASurrogatePair()
    {
        // Two characters fit; the second would be half of the emoji.
        string result = TerminalSequences.Sanitize("a\U0001F600b", 2, keepSemicolons: true);

        Assert.AreEqual("a", result);
    }

    [TestMethod]
    public void Notification_Osc777_CarriesTitleAndBody_WithoutSemicolons()
    {
        string sequence = TerminalSequences.Notification(TerminalNotificationStyle.Osc777, "Copy; done", "3 files; 2 MB");

        Assert.AreEqual("\u001b]777;notify;Copy, done;3 files, 2 MB\u0007", sequence);
    }

    [TestMethod]
    public void Notification_Osc9_JoinsTitleAndBody()
    {
        Assert.AreEqual("\u001b]9;Copy: done\u0007", TerminalSequences.Notification(TerminalNotificationStyle.Osc9, "Copy", "done"));
        Assert.AreEqual("\u001b]9;Copy\u0007", TerminalSequences.Notification(TerminalNotificationStyle.Osc9, "Copy", null));
    }

    [TestMethod]
    public void Notification_Osc9_CannotBeTurnedIntoTheProgressSequence()
    {
        // OSC 9;4;… is Windows Terminal's progress bar; a semicolon after a leading 4 must not survive.
        string sequence = TerminalSequences.Notification(TerminalNotificationStyle.Osc9, "4;3;50", null);

        Assert.AreEqual("\u001b]9;4,3,50\u0007", sequence);
    }

    [TestMethod]
    public void Notification_BellAndNone()
    {
        Assert.AreEqual("\u0007", TerminalSequences.Notification(TerminalNotificationStyle.Bell, "x", "y"));
        Assert.AreEqual(string.Empty, TerminalSequences.Notification(TerminalNotificationStyle.None, "x", "y"));
    }

    [TestMethod]
    public void Detect_EnvironmentOverrideWins()
    {
        Assert.AreEqual(TerminalNotificationStyle.None, TerminalSequences.DetectNotificationStyle(Env(("ITERMINAL_NOTIFY", "off"), ("WT_SESSION", "1"))));
        Assert.AreEqual(TerminalNotificationStyle.Osc777, TerminalSequences.DetectNotificationStyle(Env(("ITERMINAL_NOTIFY", "OSC777"))));
        Assert.AreEqual(TerminalNotificationStyle.Osc9, TerminalSequences.DetectNotificationStyle(Env(("ITERMINAL_NOTIFY", "osc9"), ("TMUX", "x"))));
        Assert.AreEqual(TerminalNotificationStyle.Bell, TerminalSequences.DetectNotificationStyle(Env(("ITERMINAL_NOTIFY", "bell"))));
    }

    [TestMethod]
    public void Detect_GuessesFromTheTerminal()
    {
        Assert.AreEqual(TerminalNotificationStyle.Osc9, TerminalSequences.DetectNotificationStyle(Env(("WT_SESSION", "abc"))));
        Assert.AreEqual(TerminalNotificationStyle.Osc9, TerminalSequences.DetectNotificationStyle(Env(("TERM_PROGRAM", "iTerm.app"))));
        Assert.AreEqual(TerminalNotificationStyle.Osc777, TerminalSequences.DetectNotificationStyle(Env(("VTE_VERSION", "7200"))));
        Assert.AreEqual(TerminalNotificationStyle.Osc777, TerminalSequences.DetectNotificationStyle(Env(("TERM", "foot"))));
        Assert.AreEqual(TerminalNotificationStyle.Osc777, TerminalSequences.DetectNotificationStyle(Env(("TERM_PROGRAM", "WezTerm"))));
    }

    [TestMethod]
    public void Detect_FallsBackToTheBell_ForUnknownTerminalsAndMultiplexers()
    {
        Assert.AreEqual(TerminalNotificationStyle.Bell, TerminalSequences.DetectNotificationStyle(Env()));
        Assert.AreEqual(TerminalNotificationStyle.Bell, TerminalSequences.DetectNotificationStyle(Env(("TMUX", "/tmp/tmux"), ("WT_SESSION", "1"))));
        Assert.AreEqual(TerminalNotificationStyle.Bell, TerminalSequences.DetectNotificationStyle(Env(("TERM", "screen-256color"), ("VTE_VERSION", "1"))));
        Assert.AreEqual(TerminalNotificationStyle.Bell, TerminalSequences.DetectNotificationStyle(Env(("TERM", "linux"))));
    }
}
