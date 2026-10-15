using System.Collections.Specialized;
using System.ComponentModel;

namespace iConsole.Data;

/// <summary>
/// A thread-safe ObservableCollection that releases the internal lock before firing
/// CollectionChanged / PropertyChanged notifications. This prevents deadlocks when
/// subscribers re-enter the collection (e.g. AsSnapshot, Add, Clear) from within the
/// event handler — which would otherwise deadlock because System.Threading.Lock is
/// not reentrant.
/// </summary>
public class ConcurrentObservableCollection<T> : ObservableCollection<T>
{
    private readonly Lock _lock = new();

    public ConcurrentObservableCollection()
    {
    }

    public ConcurrentObservableCollection(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    protected override void ClearItems()
    {
        // Mutate under lock, then notify outside the lock so subscribers cannot deadlock
        // by re-entering the collection on the same or another thread.
        lock (_lock)
        {
            Items.Clear();
        }
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void InsertItem(int index, T item)
    {
        int safeIndex;
        lock (_lock)
        {
            // The base Collection<T>.Add/Insert reads Count (to build `index`) outside this
            // lock. A concurrent Remove/Clear on another thread can shrink Items between that
            // read and here, leaving index stale. Clamp into range rather than throwing — this
            // collection is used for observation/snapshotting, so exact insertion position is
            // not load-bearing.
            safeIndex = Math.Clamp(index, 0, Items.Count);
            Items.Insert(safeIndex, item);
        }
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, safeIndex));
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        T item;
        lock (_lock)
        {
            item = Items[oldIndex];
            Items.RemoveAt(oldIndex);
            Items.Insert(newIndex, item);
        }
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, newIndex, oldIndex));
    }

    protected override void RemoveItem(int index)
    {
        T item;
        lock (_lock)
        {
            // If another thread modified the collection after the caller
            // computed the index, the provided index may be out of range.
            // In that case treat it as a no-op rather than throwing.
            if (index < 0 || index >= Items.Count)
            {
                return;
            }

            item = Items[index];
            Items.RemoveAt(index);
        }
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
    }

    protected override void SetItem(int index, T item)
    {
        T oldItem;
        lock (_lock)
        {
            oldItem = Items[index];
            Items[index] = item;
        }
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, oldItem, index));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
    }

    public void ReplaceAll(IList<T> newItems)
    {
        lock (_lock)
        {
            Items.Clear();
            foreach (var item in newItems)
            {
                Items.Add(item);
            }
        }
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public IList<T> AsSnapshot()
    {
        lock (_lock)
        {
            return [.. Items];
        }
    }
}
