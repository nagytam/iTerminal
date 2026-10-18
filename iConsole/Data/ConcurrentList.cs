using System.Collections;

namespace iConsole.Data;

public class ConcurrentList<T> : IList<T> where T : class
{
    private readonly List<T> _list = new() { Capacity = 1024 };
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _list.Count;
            }
        }
    }

    public bool IsReadOnly => false;

    public IList<T> AsSnapshot()
    {
        lock (_lock)
        {
            return [.. _list];
        }
    }

    public T this[int index] 
    { 
        get => _list[index]; 
        set => _list[index] = value; 
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _list.Add(item);
        }
    }

    public void Remove(T item)
    {
        lock (_lock)
        {
            _list.Remove(item);
        }
    }

    public void InsertAt(int index, T item)
    {
        lock (_lock)
        {
            _list.Insert(index, item);
        }
    }

    /// <summary>
    /// Swaps the whole content in one step. A reader (a snapshot taken by a draw on another thread) sees the
    /// old list or the new one, never the empty or half-filled list that <see cref="Clear"/> followed by
    /// <see cref="Add"/> calls would expose in between.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        List<T> replacement = [.. items];
        lock (_lock)
        {
            _list.Clear();
            _list.AddRange(replacement);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _list.Clear();
        }
    }

    public void ForEach(Action<T> action)
    {
        lock (_lock)
        {
            _list.ForEach(action);
        }
    }

    public List<T> ToList()
    {
        lock (_lock)
        {
            return [.. _list];
        }
    }

    public List<T>.Enumerator GetEnumerator()
    {
        lock (_lock)
        {
            return _list.ToList().GetEnumerator();
        }
    }

    public int IndexOf(T item)
    {
        lock (_lock)
        {
            return _list.IndexOf(item);
        }
    }

    public void Insert(int index, T item)
    {
        lock (_lock) 
        {
            _list.Insert(index, item);
        }
    }

    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            _list.RemoveAt(index);
        }
    }

    public bool Contains(T item)
    {
        lock (_lock)
        {
            return _list.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_lock)
        {
            _list.CopyTo(array, arrayIndex);
        }
    }

    bool ICollection<T>.Remove(T item)
    {
        lock (_lock)
        {
            return _list.Remove(item);
        }
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        lock (_lock)
        {
            return _list.GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        lock (_lock)
        {
            return _list.GetEnumerator();
        }
    }
}
