namespace iConsole.Tabular;

/// <summary>One column a grid query can name: its name and a short type, such as <c>int64</c> or <c>TEXT</c>; the type may be empty.</summary>
public sealed record GridColumnSchema(string Name, string Type = "");

/// <summary>One table a grid query can read. <see cref="Rows"/> is the row count when it is known.</summary>
public sealed record GridTableSchema(string Name, IReadOnlyList<GridColumnSchema> Columns, long? Rows = null);

/// <summary>
/// What an AI draft is told about the data: the kind of source (for example "CSV file") and its tables. It holds names, types and
/// counts only, never cell values, so a question can be answered without the data leaving the machine.
/// </summary>
public sealed record GridSchema(string Kind, IReadOnlyList<GridTableSchema> Tables)
{
    /// <summary>The schema of a source with one table, such as a file.</summary>
    public static GridSchema ForSingleTable(string kind, IReadOnlyList<GridColumnSchema> columns, long? rows = null) =>
        new(kind, [new GridTableSchema("data", columns, rows)]);
}

/// <summary>
/// Turns a question in plain language into one query in the grid query language (<see cref="GridQuery"/>). An implementation
/// asks the connected AI model; the answer is only ever shown in the query line, and it runs only when the user presses Enter.
/// </summary>
public interface IGridQueryAssistant
{
    /// <summary>Why the assistant cannot answer (AI is off, no model), or null when it can.</summary>
    string? UnavailableReason { get; }

    /// <summary>The model's answer: the query text, not yet checked against the schema. Throws when the model fails.</summary>
    Task<string> DraftAsync(string question, GridSchema schema, CancellationToken cancellationToken);
}

/// <summary>The assistant a viewer uses when none is registered: it answers that AI is not available, and never asks a model.</summary>
public sealed class UnavailableGridQueryAssistant : IGridQueryAssistant
{
    public string? UnavailableReason => "AI is not set up";

    public Task<string> DraftAsync(string question, GridSchema schema, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(UnavailableReason);
}
