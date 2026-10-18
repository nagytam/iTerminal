using iTerminal.Core.Services.Translation;

namespace iTerminal.Core.Test.Services.Translation;

[TestClass]
public class TranslationServiceTest
{
    internal sealed class FakeProvider(string id, int maxChunk = 1000) : ITranslationProvider
    {
        public string Id => id;
        public string Name => id == "api" ? "Translation API" : "AI model";
        public int MaxChunkLength => maxChunk;
        public string? UnavailableReason { get; set; }
        public Exception? Failure { get; set; }
        public string? ReportedSource { get; set; }
        public LanguageDetection? Detection { get; set; }
        public List<(string Text, string? Source, string Target)> Calls { get; } = [];
        public int DetectCalls { get; private set; }

        public Task<TranslationResult> TranslateAsync(string text, string? sourceCode, string targetCode, CancellationToken cancellationToken = default)
        {
            Calls.Add((text, sourceCode, targetCode));
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new TranslationResult($"<{text}>", ReportedSource, Name));
        }

        public Task<LanguageDetection?> DetectAsync(string text, CancellationToken cancellationToken = default)
        {
            DetectCalls++;
            return Task.FromResult(Detection);
        }
    }

    private static TranslationService Create(params ITranslationProvider[] providers) => new(new LanguageDetector(), providers);

    [TestMethod]
    public async Task TheApiIsTriedBeforeTheModelWhateverTheRegistrationOrder()
    {
        var api = new FakeProvider("api");
        var llm = new FakeProvider("llm");

        TranslationResult result = await Create(llm, api).TranslateAsync("hi", "en", "de", TranslationEngine.Auto);

        Assert.AreEqual("Translation API", result.EngineName);
        Assert.AreEqual(0, llm.Calls.Count);
    }

    [TestMethod]
    public async Task AFailingApiFallsBackToTheModel()
    {
        var api = new FakeProvider("api") { Failure = new TranslationException("Translation API is unreachable.") };
        var llm = new FakeProvider("llm");

        TranslationResult result = await Create(api, llm).TranslateAsync("hi", "en", "de", TranslationEngine.Auto);

        Assert.AreEqual("AI model", result.EngineName);
        Assert.AreEqual("<hi>", result.Text);
    }

    [TestMethod]
    public async Task AnApiThatIsNotSetUpIsSkippedWithoutBeingCalled()
    {
        var api = new FakeProvider("api") { UnavailableReason = "no translation API set up" };
        var llm = new FakeProvider("llm");

        await Create(api, llm).TranslateAsync("hi", "en", "de", TranslationEngine.Auto);

        Assert.AreEqual(0, api.Calls.Count);
        Assert.AreEqual(1, llm.Calls.Count);
    }

    [TestMethod]
    public async Task AChosenEngineIsNotFallenBackFrom()
    {
        var api = new FakeProvider("api") { Failure = new TranslationException("Translation API is unreachable.") };
        var llm = new FakeProvider("llm");

        TranslationException exception = await Assert.ThrowsExactlyAsync<TranslationException>(
            () => Create(api, llm).TranslateAsync("hi", "en", "de", TranslationEngine.Api));

        StringAssert.Contains(exception.Message, "unreachable");
        Assert.AreEqual(0, llm.Calls.Count);
    }

    [TestMethod]
    public async Task WhenNoEngineWorksTheMessageSaysWhyForEach()
    {
        var api = new FakeProvider("api") { UnavailableReason = "no translation API set up" };
        var llm = new FakeProvider("llm") { Failure = new TranslationException("AI model failed: timeout") };

        TranslationException exception = await Assert.ThrowsExactlyAsync<TranslationException>(
            () => Create(api, llm).TranslateAsync("hi", "en", "de", TranslationEngine.Auto));

        StringAssert.Contains(exception.Message, "no translation API set up");
        StringAssert.Contains(exception.Message, "timeout");
    }

    [TestMethod]
    public async Task TheSourceIsFilledInOfflineWhenTheEngineDoesNotReportIt()
    {
        var llm = new FakeProvider("llm");

        TranslationResult result = await Create(llm).TranslateAsync(
            "Der Hund ist nicht in dem Haus, aber ich habe ihn mit der Katze gesehen.", TranslationLanguages.Auto, "en", TranslationEngine.Auto);

        Assert.AreEqual("de", result.DetectedSource);
        Assert.IsNull(llm.Calls[0].Source, "auto is passed on as no source");
    }

    [TestMethod]
    public async Task AnEnginesOwnDetectionWinsOverTheOfflineGuess()
    {
        var api = new FakeProvider("api") { ReportedSource = "nl" };

        TranslationResult result = await Create(api).TranslateAsync("Der Hund ist nicht in dem Haus.", null, "en", TranslationEngine.Auto);

        Assert.AreEqual("nl", result.DetectedSource);
    }

    [TestMethod]
    public async Task AGivenSourceIsNotDetected()
    {
        TranslationResult result = await Create(new FakeProvider("llm")).TranslateAsync("hi", "fr", "de", TranslationEngine.Auto);

        Assert.IsNull(result.DetectedSource);
    }

    [TestMethod]
    public async Task AnEmptyTextIsNotSentAnywhere()
    {
        var api = new FakeProvider("api");

        TranslationResult result = await Create(api).TranslateAsync("  \n ", null, "de", TranslationEngine.Auto);

        Assert.AreEqual(string.Empty, result.Text);
        Assert.AreEqual(0, api.Calls.Count);
    }

    [TestMethod]
    public async Task AnUnknownTargetIsRefusedBeforeAnyRequest()
    {
        var api = new FakeProvider("api");

        await Assert.ThrowsExactlyAsync<TranslationException>(() => Create(api).TranslateAsync("hi", null, "xx", TranslationEngine.Auto));
        Assert.AreEqual(0, api.Calls.Count);
    }

    [TestMethod]
    public async Task ALongTextGoesInPiecesAndComesBackWithItsBlankSpaceIntact()
    {
        var api = new FakeProvider("api", maxChunk: 20);
        string text = "First paragraph.\n\nSecond paragraph here.\n  Third, indented.\n";

        TranslationResult result = await Create(api).TranslateAsync(text, "en", "de", TranslationEngine.Api);

        Assert.IsTrue(api.Calls.Count > 1, "split into several requests");
        Assert.IsTrue(api.Calls.All(call => call.Text.Length <= 20), "none over the limit");
        Assert.AreEqual(text.Length + 2 * api.Calls.Count, result.Text.Length, "only the brackets the fake adds");
        Assert.AreEqual(text, result.Text.Replace("<", string.Empty).Replace(">", string.Empty));
    }

    [TestMethod]
    public async Task OnceAPieceHasDetectedTheSourceTheRestAreGivenIt()
    {
        var api = new FakeProvider("api", maxChunk: 15) { ReportedSource = "en" };

        await Create(api).TranslateAsync("One two three four five six seven", null, "de", TranslationEngine.Api);

        Assert.IsNull(api.Calls[0].Source);
        Assert.IsTrue(api.Calls.Skip(1).All(call => call.Source == "en"), "the pieces are not each detected on their own");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("short")]
    [DataRow("exactly twenty chars")]
    [DataRow("a line\nand another line\nand a third one that goes on\n")]
    [DataRow("one sentence. Another sentence! A third? And a fourth one that is rather long indeed.")]
    [DataRow("no-spaces-at-all-in-this-very-long-run-of-characters-so-it-must-be-cut-hard")]
    [DataRow("日本語の文章です。もう一つの文章です。さらに三つ目の文章がここにあります。")]
    public void ChunksAlwaysJoinBackIntoTheTextAndStayWithinTheLimit(string text)
    {
        List<string> chunks = [.. TranslationService.Chunk(text, 20)];

        Assert.AreEqual(text, string.Concat(chunks));
        Assert.IsTrue(chunks.All(chunk => chunk.Length <= 20), string.Join("|", chunks));
    }

    [TestMethod]
    public async Task AConfidentOfflineGuessIsTakenWithoutAskingAnEngine()
    {
        var api = new FakeProvider("api") { Detection = new LanguageDetection("nl", 0.9) };

        LanguageDetection? detection = await Create(api).DetectAsync("これは日本語の文章です。ひらがなとカタカナがあります。", TranslationEngine.Auto);

        Assert.AreEqual("ja", detection!.Code);
        Assert.AreEqual(0, api.DetectCalls);
    }

    [TestMethod]
    public async Task AnUnsureOfflineGuessAsksTheEngine()
    {
        var api = new FakeProvider("api") { Detection = new LanguageDetection("sv", 0.95) };

        LanguageDetection? detection = await Create(api).DetectAsync("the cat", TranslationEngine.Auto);

        Assert.AreEqual("sv", detection!.Code);
    }

    [TestMethod]
    public async Task AChosenEngineIsAskedEvenWhenTheOfflineGuessIsSure()
    {
        var llm = new FakeProvider("llm") { Detection = new LanguageDetection("zh", 0.8) };

        LanguageDetection? detection = await Create(llm).DetectAsync("これは日本語の文章です。ひらがなとカタカナがあります。", TranslationEngine.Llm);

        Assert.AreEqual("zh", detection!.Code);
    }

    [TestMethod]
    public async Task WhenTheEnginesFailTheOfflineGuessStands()
    {
        var api = new FakeProvider("api") { Detection = null, UnavailableReason = "no translation API set up" };

        LanguageDetection? detection = await Create(api).DetectAsync("the cat", TranslationEngine.Auto);

        Assert.AreEqual("en", detection!.Code);
    }

    [TestMethod]
    public void AvailabilityNamesTheReasonOnlyWhenNothingCanDoTheWork()
    {
        var api = new FakeProvider("api") { UnavailableReason = "no translation API set up" };
        var llm = new FakeProvider("llm");
        TranslationService service = Create(api, llm);

        Assert.IsNull(service.UnavailableReason(TranslationEngine.Auto), "the model can do it");
        Assert.AreEqual("no translation API set up", service.UnavailableReason(TranslationEngine.Api));
        Assert.IsNull(service.UnavailableReason(TranslationEngine.Llm));

        llm.UnavailableReason = "AI is off (AI:Provider)";
        StringAssert.Contains(service.UnavailableReason(TranslationEngine.Auto), "AI is off");
        StringAssert.Contains(service.UnavailableReason(TranslationEngine.Auto), "no translation API");
    }
}
