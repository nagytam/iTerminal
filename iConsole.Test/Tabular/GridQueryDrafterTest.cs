using iConsole.Tabular;

namespace iConsole.Test.Tabular;

[TestClass]
public class GridQueryDrafterTest
{
    private static readonly GridSchema Sales = GridSchema.ForSingleTable(
        "CSV file",
        [new GridColumnSchema("name"), new GridColumnSchema("amount"), new GridColumnSchema("city")],
        rows: 4);

    private static readonly GridSchema TwoTables = new(
        "SQLite database",
        [
            new GridTableSchema("albums", [new GridColumnSchema("title"), new GridColumnSchema("year")]),
            new GridTableSchema("artists", [new GridColumnSchema("name")]),
        ]);

    /// <summary>A model that answers with a fixed text, or fails, and remembers what it was asked.</summary>
    private sealed class FakeAssistant : IGridQueryAssistant
    {
        public string? UnavailableReason { get; set; }
        public string Answer { get; set; } = string.Empty;
        public Func<string, string>? Respond { get; set; }
        public Exception? Failure { get; set; }
        public string? Question { get; private set; }
        public GridSchema? Schema { get; private set; }

        public Task<string> DraftAsync(string question, GridSchema schema, CancellationToken cancellationToken)
        {
            Question = question;
            Schema = schema;
            if (Failure is { } failure)
            {
                throw failure;
            }

            return Task.FromResult(Respond is { } respond ? respond(question) : Answer);
        }
    }

    [TestMethod]
    public void AQuestionIsALineThatStartsWithAQuestionMark()
    {
        Assert.IsTrue(GridQueryDrafter.IsQuestion("  ? rows where amount is over 10"));
        Assert.IsFalse(GridQueryDrafter.IsQuestion("SELECT * FROM t"));
        Assert.AreEqual("rows where amount is over 10", GridQueryDrafter.QuestionOf("  ? rows where amount is over 10  "));
    }

    [TestMethod]
    public void CleanKeepsTheQueryAndDropsTheCommentary()
    {
        Assert.AreEqual("amount > 10", GridQueryDrafter.Clean("amount > 10"));
        Assert.AreEqual("SELECT name WHERE amount > 10", GridQueryDrafter.Clean("Query: SELECT name WHERE amount > 10;"));
        Assert.AreEqual("SELECT name", GridQueryDrafter.Clean("```sql\nSELECT name\n```"));
        Assert.AreEqual("SELECT name", GridQueryDrafter.Clean("SELECT name\nThis lists the names."));
        Assert.AreEqual("", GridQueryDrafter.Clean("   "));
    }

    [TestMethod]
    public void ADraftThatFitsTheTableIsAccepted()
    {
        GridDraft draft = GridQueryDrafter.Check("SELECT name WHERE amount > 10", Sales);

        Assert.IsNull(draft.Error);
        Assert.AreEqual("SELECT name WHERE amount > 10", draft.Query);
        Assert.AreEqual("data", draft.Table!.Name);
        Assert.AreEqual(new GridComparison("amount", ">", "10"), draft.Plan!.Where!.Groups[0][0]);
    }

    [TestMethod]
    public void ADraftNamingAColumnTheTableLacksIsRefusedWithTheColumns()
    {
        GridDraft draft = GridQueryDrafter.Check("WHERE salary > 10", Sales);

        Assert.IsNull(draft.Query);
        StringAssert.Contains(draft.Error!, "salary");
        StringAssert.Contains(draft.Error!, "name, amount, city");
    }

    [TestMethod]
    public void AFromNamingATableChoosesThatTable()
    {
        GridDraft draft = GridQueryDrafter.Check("SELECT name FROM artists", TwoTables);

        Assert.IsNull(draft.Error);
        Assert.AreEqual("artists", draft.Table!.Name);
    }

    [TestMethod]
    public void ATableNameThatIsNotThereIsRefused()
    {
        GridDraft draft = GridQueryDrafter.Check("SELECT title FROM songs", TwoTables);

        Assert.IsNull(draft.Query);
        StringAssert.Contains(draft.Error!, "songs");
    }

    [TestMethod]
    public void AnEmptyAnswerIsRefused()
    {
        GridDraft draft = GridQueryDrafter.Check("   ", Sales);

        Assert.IsNull(draft.Query);
        Assert.IsNotNull(draft.Error);
    }

    [TestMethod]
    public void AColumnErrorIsReportedAgainstTheTableTheFromNames()
    {
        // year is a column of albums, not of artists: the error is about artists, not "no table called artists".
        GridDraft draft = GridQueryDrafter.Check("SELECT year FROM artists", TwoTables);

        Assert.IsNull(draft.Query);
        StringAssert.Contains(draft.Error!, "year");
        StringAssert.Contains(draft.Error!, "name");
        Assert.IsFalse(draft.Error!.Contains("no table", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AMultiTableDraftWithoutAFromAsksForTheTable()
    {
        GridDraft draft = GridQueryDrafter.Check("WHERE title = 'x'", TwoTables);

        Assert.IsNull(draft.Query);
        StringAssert.Contains(draft.Error!, "albums, artists");
    }

    [TestMethod]
    public void ADatabaseWithNoTablesSaysSo()
    {
        GridDraft draft = GridQueryDrafter.Check("SELECT *", new GridSchema("SQLite database", []));

        Assert.AreEqual("There are no tables to query", draft.Error);
    }

    [TestMethod]
    public async Task TheDraftLandsOnceAndOnlyForTheNewestQuestion()
    {
        FakeAssistant assistant = new() { Answer = "amount > 10" };
        GridQueryDrafter drafter = new(assistant);

        drafter.Start("? big sales", Sales);
        await drafter.Request!;
        GridDraft? first = drafter.TakeDraft();
        GridDraft? second = drafter.TakeDraft();

        Assert.AreEqual("big sales", assistant.Question);
        Assert.AreSame(Sales, assistant.Schema);
        Assert.AreEqual("amount > 10", first!.Query);
        Assert.IsNull(second, "a draft is taken once");
    }

    [TestMethod]
    public async Task AnAnswerOvertakenByANewerQuestionIsDropped()
    {
        FakeAssistant assistant = new() { Respond = question => $"SELECT name WHERE city = '{question}'" };
        GridQueryDrafter drafter = new(assistant);

        drafter.Start("? first", Sales);
        Task<string> overtaken = drafter.Request!;
        drafter.Start("? second", Sales);
        await overtaken;
        await drafter.Request!;

        GridDraft? draft = drafter.TakeDraft();

        Assert.AreEqual("SELECT name WHERE city = 'second'", draft!.Query, "only the newer question's answer lands, whichever finished first");
    }

    [TestMethod]
    public async Task CancelDropsTheQuestionInProgress()
    {
        FakeAssistant assistant = new() { Answer = "amount > 10" };
        GridQueryDrafter drafter = new(assistant);

        drafter.Start("? first", Sales);
        Task<string> request = drafter.Request!;
        drafter.Cancel();
        try
        {
            // Cancelled before the work started, the request is cancelled rather than finished; either way it is never taken.
            await request;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.IsNull(drafter.TakeDraft());
        Assert.IsFalse(drafter.IsBusy);
    }

    [TestMethod]
    public void AQuestionWithNoAiSaysSoAtOnceAndAsksNoModel()
    {
        FakeAssistant assistant = new() { UnavailableReason = "AI is off (AI:Provider)", Answer = "amount > 10" };
        GridQueryDrafter drafter = new(assistant);

        drafter.Start("? big sales", Sales);

        GridDraft? draft = drafter.TakeDraft();
        Assert.AreEqual("AI is off (AI:Provider)", draft!.Error);
        Assert.IsNull(assistant.Question, "the model is not asked");
    }

    [TestMethod]
    public async Task AModelFailureIsReportedNotThrown()
    {
        FakeAssistant assistant = new() { Failure = new HttpRequestException("connection refused") };
        GridQueryDrafter drafter = new(assistant);

        drafter.Start("? big sales", Sales);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => drafter.Request!);

        GridDraft? draft = drafter.TakeDraft();
        Assert.AreEqual("connection refused", draft!.Error);
    }

    [TestMethod]
    public void AnEmptyQuestionAsksNothing()
    {
        FakeAssistant assistant = new() { Answer = "amount > 10" };
        GridQueryDrafter drafter = new(assistant);

        drafter.Start("?   ", Sales);

        StringAssert.Contains(drafter.TakeDraft()!.Error!, "?");
        Assert.IsNull(assistant.Question);
    }

    [TestMethod]
    public void TheUnavailableAssistantNeverAnswers()
    {
        GridQueryDrafter drafter = new(new UnavailableGridQueryAssistant());

        drafter.Start("? anything", Sales);

        Assert.AreEqual("AI is not set up", drafter.TakeDraft()!.Error);
    }
}
