using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class TerminalProgressServiceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Update_WhenEnabled_WritesTheSequence()
    {
        var output = new StringWriter();
        var service = new TerminalProgressService(() => output, enabled: true);

        service.Update(TerminalProgressState.Normal, 30);

        Assert.AreEqual("\u001b]9;4;1;30\u0007", output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Update_WhenDisabled_WritesNothing()
    {
        var output = new StringWriter();
        var service = new TerminalProgressService(() => output, enabled: false);

        service.Update(TerminalProgressState.Normal, 30);

        Assert.AreEqual(string.Empty, output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Update_WhileTheTerminalIsLent_IsNotWrittenThenResentWhenItComesBack()
    {
        var output = new StringWriter();
        var ownership = new ConsoleOwnership();
        var service = new TerminalProgressService(() => output, ownership, enabled: true);

        // The borrower stands in for a child shell: an update made while it has the terminal is refused.
        await ownership.LendAsync(() =>
        {
            service.Update(TerminalProgressState.Normal, 60);
            return 0;
        });

        // The last requested state is written again when the terminal is reacquired.
        Assert.AreEqual("\u001b]9;4;1;60\u0007", output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Reacquire_AfterAFinalRemoveWhileLent_SendsTheRemove()
    {
        var output = new StringWriter();
        var ownership = new ConsoleOwnership();
        var service = new TerminalProgressService(() => output, ownership, enabled: true);
        service.Update(TerminalProgressState.Normal, 10);
        output.GetStringBuilder().Clear();

        // The shutdown's Remove comes while a child has the terminal: it must not be lost.
        await ownership.LendAsync(() =>
        {
            service.Update(TerminalProgressState.Remove, 0);
            return 0;
        });

        Assert.AreEqual("\u001b]9;4;0\u0007", output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Reacquire_BeforeAnyUpdate_WritesNothing()
    {
        var output = new StringWriter();
        var ownership = new ConsoleOwnership();
        _ = new TerminalProgressService(() => output, ownership, enabled: true);

        await ownership.LendAsync(() => 0);

        Assert.AreEqual(string.Empty, output.ToString());
    }
}
