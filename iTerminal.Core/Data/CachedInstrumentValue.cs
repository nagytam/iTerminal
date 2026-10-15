namespace iTerminal.Core.Data;

public class CachedInstrumentValue
{
    public int Id { get; set; }
    public required string InstrumentName { get; set; }
    public required string Currency { get; set; }
    public required decimal Value { get; set; }
    public int InstrumentType { get; set; }
    public int InstrumentValueType { get; set; }
    public DateTime SourceDateTime { get; set; }
    public DateTime CachedAtUtc { get; set; }
}
