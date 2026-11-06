using System.Collections;

namespace iConsole.Data;

public class ConcurrentList<T> : IList<T> where T : class
{
    private readonly List<T> _list = new() { Capacity = 1024 };
    private readonly Lock _lock = new();

    // The content as an array, handed out again and again until the list changes. A walk of the control tree
    // that takes a copy of every node's children on every pass allocated tens of kilobytes a pass.
    private T[]? _array;

    // The same for a foreach, whose enumerator walks a list that nothing writes to.
    private List<T>? _copy;

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

    /// <summary>
    /// The content as an array that is shared until the list next changes, for a walk that must not allocate.
    /// The caller must not write to it. A change made while it is read goes to a new array, so the walk keeps
    /// seeing the content as it was when it was taken.
    /// </summary>
    public T[] AsArray()
    {
        lock (_lock)
        {
            return _array ??= _list.ToArray();
        }
    }

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
        set
        {
            lock (_lock)
            {
                _list[index] = value;
                _array = null;
                _copy = null;
            }
        } 
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            _list.Add(item);
            _array = null;
            _copy = null;
        }
    }

    public void Remove(T item)
    {
        lock (_lock)
        {
            _list.Remove(item);
            _array = null;
            _copy = null;
        }
    }

    public void InsertAt(int index, T item)
    {
        lock (_lock)
        {
            _list.Insert(index, item);
            _array = null;
            _copy = null;
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
            _array = null;
            _copy = null;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _list.Clear();
            _array = null;
            _copy = null;
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
            return (_copy ??= [.. _list]).GetEnumerator();
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
            _array = null;
            _copy = null;
        }
    }

    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            _list.RemoveAt(index);
            _array = null;
            _copy = null;
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
            _array = null;
            _copy = null;
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
