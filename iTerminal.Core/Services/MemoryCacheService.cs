using Microsoft.Extensions.Caching.Memory;

namespace iTerminal.Core.Services;

public class MemoryCacheService : IMemoryCacheService
{
    private TimeSpan DefaultTimeSpan { get; } = new TimeSpan(0, 0, 5);
    private MemoryCache memoryCache { get; set; } = 
        new MemoryCache(new MemoryCacheOptions());

    public void Set(string key, object value)
    {
        memoryCache.Set(key, value, DefaultTimeSpan);
    }

    public void Set(string key, object value, TimeSpan timeSpan)
    {
        memoryCache.Set(key, value, timeSpan);
    }

    public object Get(string key)
    {
        return memoryCache.Get(key);
    }
}
