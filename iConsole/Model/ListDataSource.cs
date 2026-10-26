using System.Collections.Specialized;

namespace iConsole.Model;

/// <summary>
/// An <see cref="IVirtualDataSource{T}"/> over a list that is already in memory: every row is there at once, so
/// <see cref="Prefetch"/> does nothing. Hand it a list that can be read by index from the draw thread; call
/// <see cref="Refresh"/> after the list changes, or pass one that raises <see cref="INotifyCollectionChanged"/> and it does so itself.
/// </summary>
public sealed class ListDataSource<T> : IVirtualDataSource<T>
{
    private readonly IReadOnlyList<T> _items;

    public ListDataSource(IReadOnlyList<T> items)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        if (items is INotifyCollectionChanged notify)
        {
            notify.CollectionChanged += (_, _) => Refresh();
        }
    }

    public int Count => _items.Count;

    public event EventHandler? Changed;

    public bool TryGet(int index, out T? item)
    {
        // The list can shrink between the check and the read when another thread changes it; that row is simply not there.
        try
        {
            if (index >= 0 && index < _items.Count)
            {
                item = _items[index];
                return true;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        item = default;
        return false;
    }

    public void Prefetch(int start, int count)
    {
    }

    /// <summary>Tells the controls the list changed.</summary>
    public void Refresh() => Changed?.Invoke(this, EventArgs.Empty);
}
