using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class Osc52TextClipboardServiceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void EncodeSequence_IsOsc52_ForTheClipboard_BaseSixtyFour_AndBel()
    {
        // "Hi" is SGk= in base64.
        Assert.AreEqual("\u001b]52;c;SGk=\u0007", Osc52TextClipboardService.EncodeSequence("Hi"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EncodeSequence_CarriesNonAsciiTextAsUtf8()
    {
        var sequence = Osc52TextClipboardService.EncodeSequence("日本");

        // "日本" is E6 97 A5 E6 9C AC in UTF-8, which is 5pel5pys in base64.
        Assert.AreEqual("\u001b]52;c;5pel5pys\u0007", sequence);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SetText_WritesOneSequenceToTheTerminal()
    {
        var output = new StringWriter();
        var service = new Osc52TextClipboardService(() => output);

        await service.SetTextAsync("copy me");

        Assert.AreEqual(Osc52TextClipboardService.EncodeSequence("copy me"), output.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SetText_RefusesATextTooLargeForTheTerminal_RatherThanTruncatingIt()
    {
        var output = new StringWriter();
        var service = new Osc52TextClipboardService(() => output);
        // 80 000 characters become 106 668 once base64 encoded: over the limit.
        var text = new string('a', 80_000);

        await Assert.ThrowsExactlyAsync<IOException>(() => service.SetTextAsync(text));
        Assert.AreEqual(string.Empty, output.ToString(), "Nothing is sent when the text is refused.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SetText_RefusesWhileTheTerminalIsLent()
    {
        var output = new StringWriter();
        var ownership = new ConsoleOwnership();
        var service = new Osc52TextClipboardService(() => output, ownership);
        var release = new TaskCompletionSource();
        var lending = ownership.LendAsync(() => { release.Task.Wait(); return 0; });
        while (!ownership.IsLent)
        {
            await Task.Yield();
        }

        await Assert.ThrowsExactlyAsync<IOException>(() => service.SetTextAsync("x"));
        Assert.AreEqual(string.Empty, output.ToString());

        release.SetResult();
        await lending;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetText_IsNotSupported_BecauseTheTerminalCannotBeRead()
    {
        var service = new Osc52TextClipboardService(() => TextWriter.Null);

        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.GetTextAsync());
    }
}
