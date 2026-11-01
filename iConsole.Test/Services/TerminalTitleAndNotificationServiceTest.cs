using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class TerminalTitleAndNotificationServiceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Title_FirstChange_SavesTheOldTitle_ThenSetsTheNewOne()
    {
        var output = new StringWriter();
        var service = new TerminalTitleService(() => output, enabled: true);

        service.Title = "Docs";

        Assert.AreEqual(TerminalSequences.PushTitle + "\u001b]2;Docs\u0007", output.ToString());
        Assert.AreEqual("Docs", service.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Title_SecondChange_DoesNotPushAgain_AndSameTitleWritesNothing()
    {
        var output = new StringWriter();
        var service = new TerminalTitleService(() => output, enabled: true);
        service.Title = "A";
        output.GetStringBuilder().Clear();

        service.Title = "A";
        Assert.AreEqual(string.Empty, output.ToString());

        service.Title = "B";
        Assert.AreEqual("\u001b]2;B\u0007", output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Restore_PopsOnce_AndIsHarmlessWhenNothingWasSet()
    {
        var output = new StringWriter();
        var service = new TerminalTitleService(() => output, enabled: true);

        service.Restore();
        Assert.AreEqual(string.Empty, output.ToString());

        service.Title = "A";
        output.GetStringBuilder().Clear();
        service.Restore();
        service.Restore();

        Assert.AreEqual(TerminalSequences.PopTitle, output.ToString());
        Assert.AreEqual(string.Empty, service.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Title_WhenDisabled_WritesNothing()
    {
        var output = new StringWriter();
        var service = new TerminalTitleService(() => output, enabled: false);

        service.Title = "A";

        Assert.AreEqual(string.Empty, output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Title_WhileTheTerminalIsLent_IsNotWritten_AndIsSetOnceItIsBack()
    {
        var output = new StringWriter();
        var ownership = new ConsoleOwnership();
        var service = new TerminalTitleService(() => output, ownership, enabled: true);

        string duringLend = string.Empty;
        ownership.LendAsync(() =>
        {
            service.Title = "Lent";
            duringLend = output.ToString();
            return 0;
        }).GetAwaiter().GetResult();

        Assert.AreEqual(string.Empty, duringLend);
        Assert.AreEqual(string.Empty, service.Title);

        service.Title = "Lent";
        Assert.AreEqual("Lent", service.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Title_ARefusingOutput_DoesNotThrow()
    {
        var service = new TerminalTitleService(() => throw new IOException("closed"), enabled: true);

        service.Title = "A";

        Assert.AreEqual(string.Empty, service.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Notify_WritesTheSequenceOfTheStyle()
    {
        var output = new StringWriter();
        var service = new TerminalNotificationService(() => output, style: TerminalNotificationStyle.Osc777);

        bool written = service.Notify("Done", "Copied");

        Assert.IsTrue(written);
        Assert.AreEqual("\u001b]777;notify;Done;Copied\u0007", output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Notify_WithStyleNone_WritesNothing_AndBellToo()
    {
        var output = new StringWriter();
        var service = new TerminalNotificationService(() => output, style: TerminalNotificationStyle.None);

        Assert.IsFalse(service.Notify("Done"));
        service.Bell();

        Assert.AreEqual(string.Empty, output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Bell_WritesBel()
    {
        var output = new StringWriter();
        var service = new TerminalNotificationService(() => output, style: TerminalNotificationStyle.Osc9);

        service.Bell();

        Assert.AreEqual("\u0007", output.ToString());
    }
}
