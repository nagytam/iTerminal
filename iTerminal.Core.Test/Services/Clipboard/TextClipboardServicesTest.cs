using System.Runtime.Versioning;
using System.Text;
using iConsole.Services;
using iTerminal.Core.Services.Clipboard;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Clipboard;

/// <summary>
/// The text clipboard's system backends. The real Windows clipboard is not touched — a run must not overwrite what the
/// person running it had copied — so only its conversions are tested; the process-backed one runs against a fake.
/// </summary>
[TestClass]
public class TextClipboardServicesTest
{
    private sealed class ScriptedClipboard(bool failing = false) : ITextClipboardService
    {
        public string? Text { get; set; }

        public int Sets { get; private set; }

        public int Gets { get; private set; }

        public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            Sets++;
            if (failing)
            {
                throw new IOException("busy");
            }

            Text = text;
            return Task.CompletedTask;
        }

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
        {
            Gets++;
            return failing ? throw new IOException("busy") : Task.FromResult(Text);
        }
    }

    // ---- Windows conversions ----

    [Timeout(60_000)]
    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void Windows_ToClipboardBytes_UsesCrLfAndAWideTerminatingNul()
    {
        byte[] bytes = WindowsTextClipboardService.ToClipboardBytes("a\nb\r\nc\rd");
        Assert.AreEqual("a\r\nb\r\nc\r\nd\0", Encoding.Unicode.GetString(bytes));
    }

    [Timeout(60_000)]
    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void Windows_FromClipboardBytes_StopsAtTheNulAndUsesLineFeeds()
    {
        byte[] block = Encoding.Unicode.GetBytes("one\r\ntwo\rthree\0leftover");
        Assert.AreEqual("one\ntwo\nthree", WindowsTextClipboardService.FromClipboardBytes(block));
        Assert.AreEqual("plain", WindowsTextClipboardService.FromClipboardBytes(Encoding.Unicode.GetBytes("plain")));
        Assert.AreEqual(string.Empty, WindowsTextClipboardService.FromClipboardBytes([]));
    }

    [Timeout(60_000)]
    [TestMethod]
    [SupportedOSPlatform("windows")]
    public void Windows_ConversionsRoundTripNonAsciiText()
    {
        const string text = "árvíztűrő tükörfúrógép\n日本語\nemoji 😀";
        Assert.AreEqual(text, WindowsTextClipboardService.FromClipboardBytes(WindowsTextClipboardService.ToClipboardBytes(text)));
    }

    // ---- Tool-backed (Linux, macOS) ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task Process_Set_WritesTheTextToTheFirstToolThatWorks()
    {
        FakeProcessRunner runner = new();
        runner.Answer("wl-copy", string.Empty, exitCode: 1, stderr: "no wayland");
        runner.Answer("xclip", string.Empty);
        ProcessTextClipboardService service = new(runner);

        await service.SetTextAsync("héllo\nworld");

        Assert.AreEqual(2, runner.Calls.Count);
        Assert.AreEqual("wl-copy", runner.Calls[0].FileName);
        Assert.AreEqual("xclip -selection clipboard -i", runner.Calls[1].CommandLine);
        Assert.AreEqual("héllo\nworld", Encoding.UTF8.GetString(runner.Calls[1].StandardInput));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Process_Set_WithNoToolSaysSo()
    {
        ProcessTextClipboardService service = new(new FakeProcessRunner());
        IOException failure = await Assert.ThrowsExactlyAsync<IOException>(() => service.SetTextAsync("x"));
        Assert.IsTrue(failure.Message.Length > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Process_Get_ReadsFromTheFirstToolThatAnswers_NormalisingLineBreaks()
    {
        FakeProcessRunner runner = new();
        runner.Answer("wl-paste", string.Empty, exitCode: 1);
        runner.Answer("xclip", "a\r\nb");
        ProcessTextClipboardService service = new(runner);

        Assert.AreEqual("a\nb", await service.GetTextAsync());
        Assert.AreEqual("xclip -selection clipboard -o", runner.Calls[^1].CommandLine);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Process_Get_WithNoToolOrNothingOnTheClipboardIsNull()
    {
        Assert.IsNull(await new ProcessTextClipboardService(new FakeProcessRunner()).GetTextAsync());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Process_TriesTheWaylandThenTheX11ThenTheMacTool()
    {
        CollectionAssert.AreEqual(new[] { "wl-copy", "xclip", "pbcopy" }, ProcessTextClipboardService.WriteCommands().Select(c => c.Program).ToArray());
        CollectionAssert.AreEqual(new[] { "wl-paste", "xclip", "pbpaste" }, ProcessTextClipboardService.ReadCommands().Select(c => c.Program).ToArray());
    }

    // ---- Fallback ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task Fallback_WritesBothClipboardsWhileTheSystemOneWorks()
    {
        ScriptedClipboard system = new();
        ScriptedClipboard fallback = new();
        FallbackTextClipboardService service = new(system, fallback);

        await service.SetTextAsync("copied");

        Assert.AreEqual("copied", system.Text);
        Assert.AreEqual("copied", fallback.Text);
        Assert.AreEqual("copied", await service.GetTextAsync());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Fallback_AfterTheSystemClipboardFails_AnswersFromTheInProcessOneForGood()
    {
        ScriptedClipboard system = new(failing: true);
        ScriptedClipboard fallback = new();
        FallbackTextClipboardService service = new(system, fallback);

        await service.SetTextAsync("copied");
        Assert.AreEqual("copied", fallback.Text);
        Assert.AreEqual("copied", await service.GetTextAsync());
        await service.SetTextAsync("again");
        Assert.AreEqual("again", await service.GetTextAsync());
        Assert.AreEqual(1, system.Sets, "the system clipboard is not tried again once it has failed");
        Assert.AreEqual(0, system.Gets);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Fallback_AReadThatFails_FallsBackToo()
    {
        ScriptedClipboard system = new(failing: true);
        ScriptedClipboard fallback = new() { Text = "kept" };
        FallbackTextClipboardService service = new(system, fallback);
        Assert.AreEqual("kept", await service.GetTextAsync());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Fallback_ACancelledCallIsNotAFailureOfTheSystemClipboard()
    {
        CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        ScriptedClipboard system = new();
        FallbackTextClipboardService service = new(new CancelledClipboard(), system);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.SetTextAsync("x", cancelled.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.GetTextAsync(cancelled.Token));
    }

    private sealed class CancelledClipboard : ITextClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => throw new OperationCanceledException();

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => throw new OperationCanceledException();
    }

    // ---- Hosted service ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheHostedService_HandsTheApplicationsClipboardToTheControls()
    {
        ITextClipboardService original = TextClipboard.Current;
        try
        {
            ScriptedClipboard application = new();
            TextClipboardHostedService service = new(application);
            await service.StartAsync(CancellationToken.None);
            Assert.AreSame(application, TextClipboard.Current);
            await service.StopAsync(CancellationToken.None);
        }
        finally
        {
            TextClipboard.Current = original;
        }
    }
}
