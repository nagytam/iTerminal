using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace iTerminal.Core.Data;

public record InstrumentValue
{
    public required Guid Id { get; init; }
    public required Guid InstrumentId { get; init; }
    public required DateTime ValueDateTime { get; init; }
    public required decimal Value { get; init; }
    public required InstrumentValueType ValueType { get; init; }
    public required string Source { get; init; }
    public required DateTime SourceDateTime { get; init; }

    public string Key => string.Join("-", InstrumentId, ValueDateTime, ValueType);
}


