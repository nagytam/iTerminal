namespace iTerminal.Core.Data;

public class PortfolioHolding
{
    public int Id { get; set; }
    public required string Symbol { get; set; }
    public string? Exchange { get; set; }
    public decimal Quantity { get; set; }
    public decimal CostBasisPerUnit { get; set; }
    public required string Currency { get; set; }
    public DateTime? AcquiredDate { get; set; }
    public string? Notes { get; set; }
}
