using iTerminal.Core.Data;
using iTerminal.Core.Services;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Shared plumbing for quote jobs (<see cref="OnlineQuoteJob"/>, <see cref="PortfolioQuoteJob"/>):
/// hydrating <see cref="OnlineQuoteService.Instruments"/> from the SQLite instrument cache at
/// startup, and recording a freshly fetched quote (get-or-create the instrument, prepend the
/// value, persist it back to the cache).
/// </summary>
internal static class InstrumentQuoteHelper
{
    /// <summary>Populates <paramref name="onlineQuoteService"/> from previously cached instrument values.</summary>
    public static void PrimeFromCache(OnlineQuoteService onlineQuoteService, IEnumerable<CachedInstrumentValue> cachedInstruments, string source)
    {
        foreach (var cached in cachedInstruments)
        {
            var instrument = new Instrument
            {
                Id = Guid.NewGuid(),
                Currency = cached.Currency,
                Name = cached.InstrumentName,
                InstrumentType = (InstrumentType)cached.InstrumentType
            };
            var instrumentValue = new InstrumentValue
            {
                Id = Guid.NewGuid(),
                InstrumentId = instrument.Id,
                Source = source,
                SourceDateTime = cached.SourceDateTime,
                Value = cached.Value,
                ValueDateTime = cached.SourceDateTime,
                ValueType = (InstrumentValueType)cached.InstrumentValueType
            };
            instrument.InstrumentValues.InsertAt(0, instrumentValue);
            onlineQuoteService.Instruments[cached.InstrumentName] = instrument;
        }
    }

    /// <summary>
    /// Records a live intraday quote: gets or creates the named instrument, prepends the new
    /// value, and persists it to the instrument cache.
    /// </summary>
    public static async Task RecordQuoteAsync(
        OnlineQuoteService onlineQuoteService,
        ICacheService cacheService,
        string source,
        string name,
        string currency,
        InstrumentType instrumentType,
        decimal value,
        DateTime valueDateTime,
        DateTime sourceDateTime)
    {
        if (!onlineQuoteService.Instruments.TryGetValue(name, out Instrument? instrument))
        {
            instrument = new Instrument
            {
                Id = Guid.NewGuid(),
                Currency = currency,
                Name = name,
                InstrumentType = instrumentType
            };
            onlineQuoteService.Instruments[name] = instrument;
        }

        var instrumentValue = new InstrumentValue
        {
            Id = Guid.NewGuid(),
            InstrumentId = instrument.Id,
            Source = source,
            SourceDateTime = sourceDateTime,
            Value = value,
            ValueDateTime = valueDateTime,
            ValueType = InstrumentValueType.Intraday
        };
        instrument.InstrumentValues.InsertAt(0, instrumentValue);

        await cacheService.SetCachedInstrumentAsync(
            name,
            currency,
            value,
            (int)instrumentType,
            (int)InstrumentValueType.Intraday,
            valueDateTime);
    }
}
