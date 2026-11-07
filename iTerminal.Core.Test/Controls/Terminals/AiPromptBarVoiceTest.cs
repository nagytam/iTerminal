using System.Text.RegularExpressions;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.AI;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Speech;
using Microsoft.Extensions.Options;
using Moq;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Controls.Terminals;

/// <summary>Voice input in the AI bar: when its key acts, what the bar shows meanwhile, and how the words land in the prompt.</summary>
[TestClass]
public class AiPromptBarVoiceTest
{
    private sealed class FakeSpeech : ISpeechToTextService
    {
        public SpeechState State { get; set; }
        public int Toggles;
        public int Cancels;
        public string? Result;

        public event EventHandler? Changed;

        public void Toggle() => Toggles++;

        public void Cancel() => Cancels++;

        public string? TakeResult()
        {
            string? result = Result;
            Result = null;
            return result;
        }

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        public void Dispose()
        {
        }
    }

    private static (AiPromptBar Bar, AiChatModel Model, FakeSpeech Speech, Mock<IAiChatService> Chat) Create(bool available = true, bool focused = true)
    {
        var model = new AiChatModel { IsAvailable = available, ModelInfo = "m" };
        var chat = new Mock<IAiChatService>();
        chat.Setup(c => c.CheckAvailabilityAsync(It.IsAny<AiChatModel>())).Returns(Task.CompletedTask);
        chat.Setup(c => c.SendMessageAsync(It.IsAny<AiChatModel>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var theme = new TestThemeService();
        var dialog = new AiModelDialog(model, Options.Create(new AiOptions()), theme);
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.SetupGet(d => d.Now).Returns(DateTime.Now);
        var speech = new FakeSpeech();
        var bar = new AiPromptBar(model, chat.Object, dialog, dateTime.Object, theme, null, speech)
        {
            ViewSize = new Size(50, 3),
        };
        bar.OnInitializeLayout();
        if (focused)
        {
            bar.SetFocus();
        }

        return (bar, model, speech, chat);
    }

    private static KeyEvent AltF1() => new('\0', ConsoleKey.F1, false, true, false);

    private static ScreenBuffer Render(AiPromptBar bar)
    {
        var buffer = new ScreenBuffer((int)bar.ViewSize.Width, (int)bar.ViewSize.Height);
        bar.Draw(buffer);
        return buffer;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AltF1_OnAFocusedBar_StartsAndStopsTheRecording()
    {
        var (bar, _, speech, _) = Create();

        Assert.IsTrue(await bar.HandleKeyEventAsync(AltF1()));
        Assert.IsTrue(await bar.HandleKeyEventAsync(AltF1()));

        Assert.AreEqual(2, speech.Toggles);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AltF1_OnAnUnfocusedBar_IsNotTaken()
    {
        var (bar, _, speech, _) = Create(focused: false);

        Assert.IsFalse(await bar.HandleKeyEventAsync(AltF1()));

        Assert.AreEqual(0, speech.Toggles);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AltF1_WhileTheAssistantIsUnavailable_IsNotTaken()
    {
        var (bar, _, speech, _) = Create(available: false);

        Assert.IsFalse(await bar.HandleKeyEventAsync(AltF1()));

        Assert.AreEqual(0, speech.Toggles);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABarWithoutVoiceInput_LeavesAltF1Alone()
    {
        var model = new AiChatModel { IsAvailable = true, ModelInfo = "m" };
        var theme = new TestThemeService();
        var bar = new AiPromptBar(model, Mock.Of<IAiChatService>(), new AiModelDialog(model, Options.Create(new AiOptions()), theme), Mock.Of<IDateTimeProviderService>(), theme)
        {
            ViewSize = new Size(50, 3),
        };
        bar.SetFocus();

        Assert.IsFalse(await bar.HandleKeyEventAsync(AltF1()));
        Assert.IsFalse(bar.GetKeyHints().Any(h => h.Key == "Alt+F1"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheKeyHint_IsListedOnlyWhileTheAssistantIsAvailable()
    {
        var (bar, model, _, _) = Create();

        Assert.IsTrue(bar.GetKeyHints().Any(h => h.Key == "Alt+F1"));

        model.IsAvailable = false;
        Assert.AreEqual(0, bar.GetKeyHints().Count());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheBarShowsWhatTheVoiceInputIsDoing_AndNothingStaleAfterwards()
    {
        var (bar, _, speech, _) = Create();

        speech.State = SpeechState.Recording;
        StringAssert.Contains(Render(bar).Line(0), "[REC ●]");

        speech.State = SpeechState.Transcribing;
        string transcribing = Render(bar).Line(0);
        StringAssert.Contains(transcribing, "[transcribing…]");
        Assert.IsFalse(transcribing.Contains("REC"), transcribing);

        speech.State = SpeechState.Idle;
        string idle = Render(bar).Line(0);
        StringAssert.Contains(idle, "[m]>");
        Assert.IsFalse(idle.Contains("transcribing") || idle.Contains("REC"), idle);
        Assert.AreEqual(idle, Render(bar).Line(0), "a second draw is the same");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheWords_AreAppendedToTheQuestion_NotSent()
    {
        var (bar, model, speech, chat) = Create();
        model.Input = "what is";

        speech.Result = "the weather\r\nin Paris ";
        bar.OnIdle();

        Assert.AreEqual("what is the weather in Paris", model.Input);
        chat.Verify(c => c.SendMessageAsync(It.IsAny<AiChatModel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheWords_NeedNoSeparator_ForAnEmptyQuestion_OrOneEndingInASpace()
    {
        var (bar, model, speech, _) = Create();

        speech.Result = "hello";
        bar.OnIdle();
        Assert.AreEqual("hello", model.Input);

        model.Input = "hello ";
        speech.Result = "world";
        bar.OnIdle();
        Assert.AreEqual("hello world", model.Input);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhatWasTypedWhileTranscribing_IsKept()
    {
        var (bar, model, speech, _) = Create();
        speech.State = SpeechState.Transcribing;
        await bar.HandleKeyEventAsync(new KeyEvent('o', ConsoleKey.O, false, false, false));
        await bar.HandleKeyEventAsync(new KeyEvent('k', ConsoleKey.K, false, false, false));

        speech.Result = "and more";
        bar.OnIdle();

        Assert.AreEqual("ok and more", model.Input);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NothingRecognised_ChangesNothing()
    {
        var (bar, model, speech, _) = Create();
        model.Input = "keep";

        speech.Result = " \r\n ";
        bar.OnIdle();

        Assert.AreEqual("keep", model.Input);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_AbandonsTheRecording_AndClearsTheQuestion()
    {
        var (bar, model, speech, _) = Create();
        model.Input = "half a question";

        await bar.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.AreEqual(1, speech.Cancels);
        Assert.AreEqual(string.Empty, model.Input);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AltF1_IsBoundToNoOtherCommand()
    {
        string root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "iTerminal.slnx")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        Assert.IsNotNull(root, "the repository root was not found");
        var chord = new Regex(@"ConsoleKey\.F1\s*,\s*false\s*,\s*true\s*,\s*false\s*\)");
        List<string> binders = [];
        foreach (string folder in new[] { "iTerminal.Core", "iConsole" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                if (chord.IsMatch(File.ReadAllText(file)))
                {
                    binders.Add(Path.GetFileName(file));
                }
            }
        }

        CollectionAssert.AreEqual(new[] { "VoiceInputCommand.cs" }, binders);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheMicButton_NamesTheKey_AndFollowsTheState()
    {
        var (bar, _, speech, _) = Create();

        StringAssert.Contains(Render(bar).Line(0), "[● Mic: Alt+F1]");

        speech.State = SpeechState.Recording;
        StringAssert.Contains(Render(bar).Line(0), "[■ Stop: Alt+F1]");

        speech.State = SpeechState.Transcribing;
        StringAssert.Contains(Render(bar).Line(0), "transcribing");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingTheMicButton_TogglesTheRecording_ButOtherClicksDoNot()
    {
        var (bar, _, speech, _) = Create(focused: false);
        string line = Render(bar).Line(0);
        int mic = line.IndexOf("[● Mic", StringComparison.Ordinal);

        Assert.IsTrue(mic > 0);
        Assert.IsFalse(await bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(mic - 5, 0), LeftClicked = true }));
        Assert.AreEqual(0, speech.Toggles);

        Assert.IsTrue(await bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(mic + 2, 0), LeftClicked = true }));
        Assert.AreEqual(1, speech.Toggles);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlF7_IsASecondKeyForTheRecording_AndHasItsOwnHint()
    {
        var (bar, _, speech, _) = Create();

        Assert.IsTrue(await bar.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.F7, false, false, true)));

        Assert.AreEqual(1, speech.Toggles);
        Assert.IsTrue(bar.GetKeyHints().Any(h => h.Key == "Ctrl+F7"));
    }
}
