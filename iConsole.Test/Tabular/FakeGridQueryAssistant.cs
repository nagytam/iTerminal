using iConsole.Tabular;

namespace iConsole.Test.Tabular;

/// <summary>A model stand-in for the viewer tests: it answers with a fixed text, or fails, and remembers what it was asked.</summary>
public sealed class FakeGridQueryAssistant(string answer = "SELECT * LIMIT 1") : IGridQueryAssistant
{
    public string? UnavailableReason { get; set; }
    public string Answer { get; set; } = answer;
    public Exception? Failure { get; set; }
    public string? Question { get; private set; }
    public GridSchema? Schema { get; private set; }
    public int Calls { get; private set; }

    public Task<string> DraftAsync(string question, GridSchema schema, CancellationToken cancellationToken)
    {
        Calls++;
        Question = question;
        Schema = schema;
        if (Failure is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(Answer);
    }
}
