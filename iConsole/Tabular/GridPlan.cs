namespace iConsole.Tabular;

/// <summary>
/// What a grid query does once parsed: the table it names (null when the query names none), the columns to show (null for all),
/// the filter, the column to order by, and the row limit. Names are spelled as the table's own headers spell them.
/// </summary>
public sealed record GridQueryPlan(
    string? From,
    IReadOnlyList<string>? Columns,
    GridCondition? Where,
    string? OrderBy,
    bool Descending,
    int? Limit);

/// <summary>A filter: groups joined by <c>OR</c>, each group a list of terms joined by <c>AND</c> (so AND binds tighter).</summary>
public sealed record GridCondition(IReadOnlyList<IReadOnlyList<GridTerm>> Groups);

/// <summary>One test on one column.</summary>
public abstract record GridTerm(string Column);

/// <summary>A comparison. <see cref="Operator"/> is one of <c>=</c> <c>!=</c> <c>&lt;</c> <c>&lt;=</c> <c>&gt;</c> <c>&gt;=</c>.</summary>
public sealed record GridComparison(string Column, string Operator, string Value) : GridTerm(Column);

/// <summary><c>LIKE</c> with <c>%</c> and <c>_</c>; <see cref="Negate"/> is <c>NOT LIKE</c>.</summary>
public sealed record GridLike(string Column, string Pattern, bool Negate) : GridTerm(Column);

/// <summary><c>CONTAINS</c>: case-insensitive text inside the value.</summary>
public sealed record GridContains(string Column, string Text) : GridTerm(Column);

/// <summary><c>IS EMPTY</c> or, with <see cref="Negate"/>, <c>IS NOT EMPTY</c>.</summary>
public sealed record GridIsEmpty(string Column, bool Negate) : GridTerm(Column);
