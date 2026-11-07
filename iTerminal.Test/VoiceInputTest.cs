using iTerminal.Core.AI;

namespace iTerminal.Test;

/// <summary>
/// Voice input end to end: the real key, the real service and the real bar, on a stand-in microphone and a stand-in
/// recogniser (the sandbox replaces the two edges, so no run opens a device or loads the 148 MB model).
/// </summary>
[TestClass]
[DoNotParallelize]
public class VoiceInputTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    /// <summary>The assistant is not reachable in the sandbox: the bar is made available by hand once its own check has answered.</summary>
    private async Task<AiChatModel> MakeTheBarAvailableAsync()
    {
        var model = _app.Driver.GetRequiredService<AiChatModel>();
        await _app.WaitUntilAsync(() => model.ModelInfo != "checking...", "the availability check to answer");
        model.ModelInfo = "sandbox";
        model.IsAvailable = true;
        await _app.WaitForTextAsync("[sandbox]>");
        return model;
    }

    /// <summary>Tab moves the focus round the panels and the bar; Alt+F1 only acts on the bar, so the first Tab after which it does is the bar's.</summary>
    private async Task StartRecordingAsync()
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            await _app.PressAsync("Tab");
            await _app.PressAsync("Alt+F1");
            try
            {
                await _app.WaitForTextAsync("[REC ●]", TimeSpan.FromSeconds(2));
                return;
            }
            catch (AssertFailedException)
            {
                // A panel had the focus; the key was not the bar's.
            }
        }

        Assert.Fail("Alt+F1 never started a recording.\n" + _app.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ASpokenQuestion_IsTypedIntoTheBar_AndNotSent()
    {
        AiChatModel model = await MakeTheBarAvailableAsync();
        await StartRecordingAsync();

        await _app.PressAsync("Alt+F1");

        var screen = await _app.WaitForTextAsync($"[sandbox]> {SandboxServices.SpeechRecognizer.Sentence}");
        Assert.IsFalse(screen.Contains("REC"), screen.Dump());
        Assert.AreEqual(SandboxServices.SpeechRecognizer.Sentence, model.Input);
        Assert.AreEqual(0, model.History.Count, "the question is typed for the user to send, not sent");
        Assert.IsFalse(model.IsThinking);
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task Escape_WhileRecording_TypesNothing()
    {
        AiChatModel model = await MakeTheBarAvailableAsync();
        await StartRecordingAsync();

        await _app.PressAsync("Esc");

        await _app.WaitForTextGoneAsync("[REC ●]");
        await Task.Delay(500);
        Assert.AreEqual(string.Empty, model.Input);
        Assert.IsFalse(_app.Screen.Contains(SandboxServices.SpeechRecognizer.Sentence), _app.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task AltF1_WithTheFocusInAPanel_DoesNotRecord()
    {
        await MakeTheBarAvailableAsync();

        await _app.PressAsync("Alt+F1");
        await Task.Delay(500);

        Assert.IsFalse(_app.Screen.Contains("[REC ●]"), _app.Screen.Dump());
    }
}
