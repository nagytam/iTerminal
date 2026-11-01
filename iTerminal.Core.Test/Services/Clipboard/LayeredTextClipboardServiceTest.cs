using iConsole.Services;
using iTerminal.Core.Services.Clipboard;

namespace iTerminal.Core.Test.Services.Clipboard;

[TestClass]
public class LayeredTextClipboardServiceTest
{
    /// <summary>A clipboard that records what it was asked and can be made to fail or to hold text.</summary>
    private sealed class FakeClipboard : ITextClipboardService
    {
        public List<string> Written { get; } = [];
        public int ReadCount { get; private set; }
        public bool FailsOnWrite { get; set; }
        public bool FailsOnRead { get; set; }
        public string? Holds { get; set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            if (FailsOnWrite)
            {
                throw new IOException("refused");
            }

            Written.Add(text);
            return Task.CompletedTask;
        }

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (FailsOnRead)
            {
                throw new IOException("no tool");
            }

            return Task.FromResult(Holds);
        }
    }

    private static (LayeredTextClipboardService Service, FakeClipboard System, FakeClipboard Terminal, FakeClipboard InProcess) Create(bool preferTerminal)
    {
        var system = new FakeClipboard();
        var terminal = new FakeClipboard();
        var inProcess = new FakeClipboard();
        return (new LayeredTextClipboardService(system, terminal, inProcess, preferTerminal), system, terminal, inProcess);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Locally_TheSystemClipboardIsTheCopyTarget_AndTheTerminalIsNotWritten()
    {
        var (service, system, terminal, inProcess) = Create(preferTerminal: false);

        await service.SetTextAsync("hello");

        CollectionAssert.AreEqual(new[] { "hello" }, system.Written.ToList());
        Assert.AreEqual(0, terminal.Written.Count);
        CollectionAssert.AreEqual(new[] { "hello" }, inProcess.Written.ToList(), "The application's own copy is always kept.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Locally_WhenTheSystemClipboardFails_TheTerminalTakesTheCopy_AndTheSystemIsNotAskedAgain()
    {
        var (service, system, terminal, _) = Create(preferTerminal: false);
        system.FailsOnWrite = true;

        await service.SetTextAsync("first");
        system.FailsOnWrite = false;
        await service.SetTextAsync("second");

        CollectionAssert.AreEqual(new[] { "first", "second" }, terminal.Written.ToList());
        Assert.AreEqual(0, system.Written.Count, "Once the system clipboard has failed it is skipped for the rest of the run.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OverSsh_TheTerminalComesFirst_AndTheRemoteSystemIsLeftAlone()
    {
        var (service, system, terminal, _) = Create(preferTerminal: true);

        await service.SetTextAsync("remote copy");

        CollectionAssert.AreEqual(new[] { "remote copy" }, terminal.Written.ToList());
        Assert.AreEqual(0, system.Written.Count, "A clipboard program on the remote machine would set one the person cannot reach.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OverSsh_WhenTheTerminalRefusesTheCopy_TheSystemClipboardTakesIt()
    {
        var (service, system, terminal, _) = Create(preferTerminal: true);
        terminal.FailsOnWrite = true;

        await service.SetTextAsync("copy");

        CollectionAssert.AreEqual(new[] { "copy" }, system.Written.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Paste_ReadsTheSystemClipboard()
    {
        var (service, system, _, _) = Create(preferTerminal: false);
        system.Holds = "from outside";

        Assert.AreEqual("from outside", await service.GetTextAsync());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Paste_WhenTheSystemClipboardHoldsNoText_PastesTheLastCopyMadeHere()
    {
        var (service, system, _, inProcess) = Create(preferTerminal: false);
        inProcess.Holds = "kept";
        system.Holds = null;

        Assert.AreEqual("kept", await service.GetTextAsync());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Paste_WhenTheSystemClipboardCannotBeRead_PastesTheLastCopyMadeHere()
    {
        var (service, system, _, inProcess) = Create(preferTerminal: false);
        inProcess.Holds = "kept";
        system.FailsOnRead = true;

        Assert.AreEqual("kept", await service.GetTextAsync());
        Assert.AreEqual(1, system.ReadCount);
        await service.GetTextAsync();
        Assert.AreEqual(1, system.ReadCount, "A clipboard that could not be read is not asked again.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsRemoteSession_IsTrueWhenAnSshVariableIsSet()
    {
        Assert.IsTrue(LayeredTextClipboardService.IsRemoteSession(name => name == "SSH_CONNECTION" ? "10.0.0.2 22 10.0.0.1 5000" : null));
        Assert.IsTrue(LayeredTextClipboardService.IsRemoteSession(name => name == "SSH_TTY" ? "/dev/pts/3" : null));
        Assert.IsFalse(LayeredTextClipboardService.IsRemoteSession(_ => null));
    }
}
