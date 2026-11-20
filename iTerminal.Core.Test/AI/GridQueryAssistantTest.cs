using iConsole.Tabular;
using iTerminal.Core.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;

namespace iTerminal.Core.Test.AI;

[TestClass]
public class GridQueryAssistantTest
{
    private static readonly GridSchema Sales = GridSchema.ForSingleTable(
        "CSV file",
        [new GridColumnSchema("name", "string"), new GridColumnSchema("amount", "int64")],
        rows: 3);

    private static (GridQueryAssistant Assistant, List<IList<ChatMessage>> Sent) Create(string answer, AiProvider provider = AiProvider.Ollama)
    {
        var sent = new List<IList<ChatMessage>>();
        var client = new Mock<IChatClient>();
        client.Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) => sent.Add([.. messages]))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        return (new GridQueryAssistant(client.Object, Options.Create(new AiOptions { Provider = provider })), sent);
    }

    [TestMethod]
    public async Task TheAnswerComesBackAsTheModelWroteIt()
    {
        // Cleaning a reply (fences, labels, commentary) and checking it against the table is the drafter's job, not the assistant's.
        (GridQueryAssistant assistant, _) = Create("SELECT name WHERE amount > 10");

        string draft = await assistant.DraftAsync("big sales", Sales, CancellationToken.None);

        Assert.AreEqual("SELECT name WHERE amount > 10", draft);
    }

    [TestMethod]
    public async Task TheModelIsToldTheNamesAndTypesButNoValues()
    {
        (GridQueryAssistant assistant, List<IList<ChatMessage>> sent) = Create("SELECT *");

        await assistant.DraftAsync("which names", Sales, CancellationToken.None);

        string user = sent.Single().Single(m => m.Role == ChatRole.User).Text;
        StringAssert.Contains(user, "Table data (3 rows): name (string), amount (int64)");
        StringAssert.Contains(user, "Question: which names");
    }

    [TestMethod]
    public void TheSystemPromptNamesTheGrammarTheParserAccepts()
    {
        StringAssert.Contains(GridQueryAssistant.SystemPrompt, "ORDER BY");
        StringAssert.Contains(GridQueryAssistant.SystemPrompt, "CONTAINS");
        StringAssert.Contains(GridQueryAssistant.SystemPrompt, "IS EMPTY");
    }

    [TestMethod]
    public async Task WithAiOffNothingIsSentAndTheReasonIsGiven()
    {
        (GridQueryAssistant assistant, List<IList<ChatMessage>> sent) = Create("SELECT *", AiProvider.None);

        Assert.IsNotNull(assistant.UnavailableReason);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => assistant.DraftAsync("anything", Sales, CancellationToken.None));
        Assert.AreEqual(0, sent.Count, "the model is not asked");
    }

    [TestMethod]
    public void WithAnAiProviderTheAssistantIsAvailable()
    {
        (GridQueryAssistant assistant, _) = Create("SELECT *", AiProvider.OpenAI);

        Assert.IsNull(assistant.UnavailableReason);
    }
}
