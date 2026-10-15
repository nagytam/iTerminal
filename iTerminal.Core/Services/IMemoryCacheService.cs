namespace iTerminal.Core.Services;

public interface IMemoryCacheService
{
    object Get(string key);
    void Set(string key, object value);
    void Set(string key, object value, TimeSpan timeSpan);
}