namespace iTerminal.Core.Services.Portfolio;

public interface IQuoteProvider
{
    Task<IReadOnlyDictionary<string, StooqQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default);
}

public record StooqQuote(string Symbol, decimal Close, DateTime Timestamp);
