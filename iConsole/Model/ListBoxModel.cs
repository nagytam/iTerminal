using iConsole.Data;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace iConsole.Model;

public class ListBoxModel<T> : IListBoxModel<T>, INotifyPropertyChanged
{
    private ConcurrentObservableCollection<T> _items;
    private readonly ConcurrentList<IListItem<T>> _listItems = new();
    private int _hasPendingUpdate = 1;
    private readonly Lock _updateLock = new();

    public ListBoxModel(ConcurrentObservableCollection<T> items)
    {
        _items = items;
        _items.CollectionChanged += OnCollectionChanged;
    }

    public ConcurrentObservableCollection<T> Items
    {
        get => _items;
        set
        {
            if (ReferenceEquals(_items, value))
            {
                return;
            }

            _items.CollectionChanged -= OnCollectionChanged;
            _items = value;
            _items.CollectionChanged += OnCollectionChanged;

            Interlocked.Exchange(ref _hasPendingUpdate, 1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ListItems));
            OnPropertyChanged(nameof(HasPendingUpdate));
        }
    }

    public IReadOnlyList<IListItem<T>> ListItems => (IReadOnlyList<IListItem<T>>)_listItems.AsSnapshot();

    public bool HasPendingUpdate => Interlocked.CompareExchange(ref _hasPendingUpdate, 0, 0) == 1;

    public Task? UpdateTask { get; private set; } = Task.CompletedTask;

    public Func<IEnumerable<T>, CancellationToken, IEnumerable<T>> FilterAndSortItems { get; set; } = FilterAndSortItemDefault;

    public Func<IListItem<T>, CancellationToken, bool> Filter { get; set; } = static (item, cancellationToken) => true;

    public Func<T, CancellationToken, IListItem<T>> TransformItem { get; set; } = TransformItemDefault;

    public Func<T, CancellationToken, bool> IsItemEnabled { get; set; } = static (item, cancellationToken) => true;

    public IListItem<T>? SelectedItem => _listItems.AsSnapshot().FirstOrDefault(item => item.IsSelected);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Interlocked.Exchange(ref _hasPendingUpdate, 1);
        OnPropertyChanged(nameof(HasPendingUpdate));
    }

    public void Invalidate()
    {
        Interlocked.Exchange(ref _hasPendingUpdate, 1);
        OnPropertyChanged(nameof(HasPendingUpdate));
    }

    public void UpdateListItems()
    {
        lock (_updateLock)
        {
            if (!HasPendingUpdate)
            {
                return;
            }

            Interlocked.Exchange(ref _hasPendingUpdate, 0);
            UpdateTask = Task.CompletedTask;
            Update(CancellationToken.None);
            OnPropertyChanged(nameof(HasPendingUpdate));
            OnPropertyChanged(nameof(UpdateTask));
        }
    }

    private void Update(CancellationToken cancellationToken)
    {
        var sourceItems = Items.AsSnapshot();
        var projectedItems = FilterAndSortItems(sourceItems, cancellationToken)
            .Select(item => ApplyIsItemEnabled(TransformItem(item, cancellationToken), item, cancellationToken))
            .Where(item => Filter(item, cancellationToken))
            .ToList();

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // One swap, not "empty it, then add the rows": a draw on another thread between those two steps saw
        // an empty list (the empty text flashing up, or a test reading no rows) while quotes, tasks or search
        // results were being refreshed from a worker.
        _listItems.ReplaceAll(projectedItems);

        OnPropertyChanged(nameof(ListItems));
        OnPropertyChanged(nameof(SelectedItem));
    }

    /// <summary>
    /// Applies <see cref="IsItemEnabled"/> on top of whatever <see cref="TransformItem"/> decided.
    /// It is a gate, not an override — it can only disable a row — so a transform that already
    /// sets <c>IsEnabled</c> keeps working. It used to be declared and never consulted, which left
    /// every list that set it (the folder selector's drive page, for one) drawing unavailable rows
    /// exactly like available ones.
    /// </summary>
    private IListItem<T> ApplyIsItemEnabled(IListItem<T> listItem, T item, CancellationToken cancellationToken)
    {
        if (listItem is ListItem<T> { IsEnabled: true } concrete && !IsItemEnabled(item, cancellationToken))
        {
            concrete.IsEnabled = false;
        }
        return listItem;
    }

    private static IEnumerable<T> FilterAndSortItemDefault(IEnumerable<T> enumerable, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return enumerable;
    }

    protected static IListItem<T> TransformItemDefault(T item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ListItem<T>(item)
        {
            IsEnabled = true
        };
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}