using iConsole.Data;

namespace iConsole.Helper;

public static class ConcurrentListExtensions
{
    public static void FromList<T>(this ConcurrentList<T> concurrentList, List<T> items)
        where T : class
    {
        concurrentList.Clear();
        foreach (var item in items)
        {
            concurrentList.Add(item);
        }
    }

}
