using iConsole.Data;

namespace iTerminal.Core.Data;

public class Instrument
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Currency { get; set; }
    public InstrumentType InstrumentType { get; set; }
    public ConcurrentList<InstrumentValue> InstrumentValues { get; set; } = [];
}
