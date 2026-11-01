using iTerminal.Core.AI;
using iTerminal.Core.Services.Translation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;

namespace iTerminal.Core.Test.Services.Translation;

[TestClass]
public class LlmTranslationProviderTest
{
    private static (LlmTranslationProvider Provider, List<IList<ChatMessage>> Sent) Create(string answer, AiProvider provider = AiProvider.Ollama)
    {
        var sent = new List<IList<ChatMessage>>();
        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) => sent.Add([.. messages]))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        return (new LlmTranslationProvider(client.Object, Options.Create(new AiOptions { Provider = provider })), sent);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheTextIsTheUserMessageAndTheInstructionsNameTheTarget()
    {
        (LlmTranslationProvider provider, List<IList<ChatMessage>> sent) = Create("Hallo Welt");

        TranslationResult result = await provider.TranslateAsync("Hello world", "en", "de");

        Assert.AreEqual("Hallo Welt", result.Text);
        Assert.AreEqual("AI model", result.EngineName);
        Assert.AreEqual(ChatRole.System, sent[0][0].Role);
        StringAssert.Contains(sent[0][0].Text, "German (de)");
        StringAssert.Contains(sent[0][0].Text, "English (en)");
        Assert.AreEqual("Hello world", sent[0][1].Text, "the text itself, with no wrapping the model could echo back");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutASourceTheModelIsToldToWorkItOut()
    {
        (LlmTranslationProvider provider, List<IList<ChatMessage>> sent) = Create("x");

        await provider.TranslateAsync("a", null, "fr");

        StringAssert.Contains(sent[0][0].Text, "not given");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReasoningBlocksAreRemovedFromTheAnswer()
    {
        (LlmTranslationProvider provider, _) = Create("<think>the user wants German</think>\nHallo Welt  \n");

        TranslationResult result = await provider.TranslateAsync("Hello world", null, "de");

        Assert.AreEqual("Hallo Welt", result.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyAnswerIsAnError()
    {
        (LlmTranslationProvider provider, _) = Create("<think>hmm</think>");

        await Assert.ThrowsExactlyAsync<TranslationException>(() => provider.TranslateAsync("Hello", null, "de"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABrokenModelServerBecomesATranslationException()
    {
        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        var provider = new LlmTranslationProvider(client.Object, Options.Create(new AiOptions { Provider = AiProvider.Ollama }));

        TranslationException exception = await Assert.ThrowsExactlyAsync<TranslationException>(() => provider.TranslateAsync("Hello", null, "de"));

        StringAssert.Contains(exception.Message, "connection refused");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhenAiIsOffNoRequestIsMade()
    {
        (LlmTranslationProvider provider, List<IList<ChatMessage>> sent) = Create("x", AiProvider.None);

        Assert.IsNotNull(provider.UnavailableReason);
        await Assert.ThrowsExactlyAsync<TranslationException>(() => provider.TranslateAsync("Hello", null, "de"));
        Assert.AreEqual(0, sent.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("de", "de")]
    [DataRow("DE.", "de")]
    [DataRow("\"pt-BR\"", "pt")]
    [DataRow("German", "de")]
    [DataRow("fr\nThe text is French.", "fr")]
    public async Task DetectionReadsACodeOrANameFromTheAnswer(string answer, string expected)
    {
        (LlmTranslationProvider provider, _) = Create(answer);

        LanguageDetection? detection = await provider.DetectAsync("Guten Tag");

        Assert.AreEqual(expected, detection!.Code);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnAnswerThatNamesNoKnownLanguageDetectsNothing()
    {
        (LlmTranslationProvider provider, _) = Create("I am not sure what this is.");

        Assert.IsNull(await provider.DetectAsync("???"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnlyTheStartOfALongTextIsSentForDetection()
    {
        (LlmTranslationProvider provider, List<IList<ChatMessage>> sent) = Create("en");

        await provider.DetectAsync(new string('a', 5000));

        Assert.AreEqual(600, sent[0][1].Text.Length);
    }
}
