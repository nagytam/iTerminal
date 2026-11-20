using System.Globalization;
using System.Text;
using iConsole.Tabular;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace iTerminal.Core.AI;

/// <summary>
/// Drafts a grid query from a question through the connected AI model, the same <see cref="IChatClient"/> as the prompt bar. The
/// model is told the grammar and the table and column names, never the cell values; the answer is checked by the caller with the
/// query parser, so a model that invents a column is refused rather than run.
/// </summary>
public sealed class GridQueryAssistant(IChatClient chatClient, IOptions<AiOptions> aiOptions) : IGridQueryAssistant
{
    public string? UnavailableReason =>
        aiOptions.Value.Provider == AiProvider.None ? "AI is off (AI:Provider)" : null;

    public async Task<string> DraftAsync(string question, GridSchema schema, CancellationToken cancellationToken)
    {
        if (UnavailableReason is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        ChatResponse response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, UserMessage(question, schema))],
            new ChatOptions { Temperature = 0 },
            cancellationToken);
        return AiChatService.Sanitize(response.Text ?? string.Empty);
    }

    /// <summary>The grammar, in the words the parser accepts. Kept short: a small local model follows a short rule list better.</summary>
    internal const string SystemPrompt =
        "You write one query for a table, in a small SQL-like language, and nothing else. " +
        "Answer with the query on one line: no explanation, no code fence, no label. " +
        "Language: SELECT col1, col2 | SELECT * (optional), FROM table (optional, needed when the data has several tables), " +
        "WHERE condition, ORDER BY col [DESC], LIMIT n. " +
        "A condition is: col = value, col != value, col < value, col <= value, col > value, col >= value, " +
        "col LIKE 'pattern' (% is any text, _ is one character), col NOT LIKE 'pattern', col CONTAINS 'text', " +
        "col IS EMPTY, col IS NOT EMPTY. Join conditions with AND and OR (AND binds tighter; no parentheses). " +
        "Put text values in single quotes; numbers bare. " +
        "Use only the table and column names listed, spelled exactly as listed. If the question cannot be answered with this language, " +
        "answer with the closest query that lists the relevant rows.";

    /// <summary>The data description and the question. Names and types only: the model never sees a cell.</summary>
    internal static string UserMessage(string question, GridSchema schema)
    {
        StringBuilder text = new();
        text.Append("Data: ").Append(schema.Kind).AppendLine();
        foreach (GridTableSchema table in schema.Tables)
        {
            string rows = table.Rows is { } count ? string.Format(CultureInfo.InvariantCulture, " ({0:N0} rows)", count) : string.Empty;
            text.Append("Table ").Append(table.Name).Append(rows).Append(": ");
            text.AppendJoin(", ", table.Columns.Select(column => string.IsNullOrEmpty(column.Type) ? column.Name : $"{column.Name} ({column.Type})"));
            text.AppendLine();
        }

        text.Append("Question: ").Append(question);
        return text.ToString();
    }
}
