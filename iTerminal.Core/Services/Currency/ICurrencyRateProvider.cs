namespace iTerminal.Core.Services.Currency;

/// <summary>Where the exchange rates come from. A test replaces it so no run reaches the network.</summary>
public interface ICurrencyRateProvider
{
    string Name { get; }

    /// <summary>Downloads the latest rates; throws <see cref="CurrencyException"/> when they cannot be had.</summary>
    Task<CurrencyRates> FetchAsync(CancellationToken cancellationToken = default);
}

public sealed class CurrencyException(string message, Exception? inner = null) : Exception(message, inner);
