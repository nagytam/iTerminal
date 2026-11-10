using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;
using iTerminal.Core.AI;
using iTerminal.Core.AI.Voice;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Dialogs;
using Microsoft.Extensions.Options;
using Moq;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Controls.Terminals;

/// <summary>The mic button of the AI bar, its hot key hint, and where dictated words land.</summary>
[TestClass]
public class AiPromptBarVoiceTest
{
    private sealed class FakeVoice : IVoiceInputService
    {
        public bool IsAvailable { get; set; } = true;
        public string UnavailableReason { get; set; } = string.Empty;
        public bool IsListening { get; private set; }
        public event EventHandler<string>? TextRecognized;
        public event EventHandler? ListeningChanged;

        public bool Start()
        {
            IsListening = true;
            ListeningChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Stop()
        {
            IsListening = false;
            ListeningChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Say(string text) => TextRecognized?.Invoke(this, text);
    }

    private static (AiPromptBar Bar, AiChatModel Model, FakeVoice Voice) Create(int width = 80)
    {
        var model = new AiChatModel { IsAvailable = true, ModelInfo = "m" };
        var chat = new Mock<IAiChatService>();
        chat.Setup(c => c.CheckAvailabilityAsync(It.IsAny<AiChatModel>())).Returns(Task.CompletedTask);
        var theme = new TestThemeService();
        var dialog = new AiModelDialog(model, Options.Create(new AiOptions()), theme);
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.SetupGet(d => d.Now).Returns(DateTime.Now);
        var voice = new FakeVoice();
        var bar = new AiPromptBar(model, chat.Object, dialog, dateTime.Object, theme, null, voice)
        {
            ViewSize = new Size(width, 2),
        };
        bar.OnInitializeLayout();
        bar.SetFocus();
        return (bar, model, voice);
    }

    private static string FirstLine(AiPromptBar bar)
    {
        var buffer = new ScreenBuffer((int)bar.ViewSize.Width, (int)bar.ViewSize.Height);
        bar.Draw(buffer);
        return buffer.Line(0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheMicButtonNamesItsKeyAndShowsWhenListening()
    {
        var (bar, _, voice) = Create();

        StringAssert.Contains(FirstLine(bar), "[Mic - Ctrl+F7]");

        bar.ToggleVoiceInput();

        Assert.IsTrue(voice.IsListening);
        StringAssert.Contains(FirstLine(bar), "Listening - Ctrl+F7");

        bar.ToggleVoiceInput();

        Assert.IsFalse(voice.IsListening);
        StringAssert.Contains(FirstLine(bar), "[Mic - Ctrl+F7]");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DictatedWordsAreAppendedToTheQuestion()
    {
        var (bar, model, voice) = Create();
        model.Input = "what is";

        voice.Say("a symlink");

        Assert.AreEqual("what is a symlink", model.Input);
        StringAssert.Contains(FirstLine(bar), "what is a symlink");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheHotKeyHintFollowsTheState()
    {
        var (bar, _, voice) = Create();

        Assert.IsTrue(bar.GetKeyHints().Any(h => h.Key == "Ctrl+F7" && h.Description.Contains("Dictate")));

        voice.Start();

        Assert.IsTrue(bar.GetKeyHints().Any(h => h.Key == "Ctrl+F7" && h.Description.Contains("Stop")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutARecognizerTheButtonSaysSoAndThereIsNoHint()
    {
        var (bar, _, voice) = Create();
        voice.IsAvailable = false;

        StringAssert.Contains(FirstLine(bar), "[Mic: n/a]");
        Assert.IsFalse(bar.GetKeyHints().Any(h => h.Key == "Ctrl+F7"));
    }
}
