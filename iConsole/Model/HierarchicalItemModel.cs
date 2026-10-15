using iConsole.Data;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace iConsole.Model;

[DebuggerDisplay("{Name}")]
public class HierarchicalItemModel : IHierarchicalItemModel
{
    public IHierarchicalItemModel? Parent { get; init; }
    public string Name { get; init; }
    public ConcurrentObservableCollection<IHierarchicalItemModel> Children { get; set; } = [];
    public Position Position { get; set; } = new(0, 0);
    public Size Size { get; set; } = new(0, 0);
    public int Layer
    {
        get 
        {
            if (Parent == null)
            {
                return 0;
            }
            else
            {
                return Parent.Count + 1;
            }
        }
    }
    private bool _isOpened = false;
    public bool IsOpened
    {
        get
        {
            return _isOpened;
        }
        set
        {
            if (_isOpened != value)
            {
                _isOpened = value;
                OnPropertyChanged(nameof(IsOpened));
                OnHierarchyChanged();
            }
        }
    }
    private bool _isFocused = false;
    public bool IsFocused
    {
        get
        {
            return _isFocused;
        }
        set
        {
            if (_isFocused != value)
            {
                _isFocused = value;
                OnPropertyChanged(nameof(IsFocused));
                OnHierarchyChanged();
            }
        }
    }

    public int Count
    {
        get
        {
            var children = Children.AsSnapshot();
            var count = children.Count;
            foreach (var child in children)
            {
                count += child.Count;
            }
            return count;
        }
    }

    public HierarchicalItemModel(IHierarchicalItemModel? parent, string name)
    {
        Name = name;
        Parent = parent;
        Children.CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnHierarchyChanged();
    }

    public virtual void OnHierarchyChanged()
    {
        if (Parent != null)
        {
            Parent.OnHierarchyChanged();
        }
        else
        {
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(Size));
        }
    }

    public IHierarchicalItemModel? GetFocusedItem()
    {
        if (IsFocused)
        {
            return this;
        }
        else
        {
            foreach (var child in Children.AsSnapshot())
            {
                var focusedItem = child.GetFocusedItem();
                if (focusedItem is not null)
                {
                    return focusedItem;
                }
            }
            return null;
        }
    }

    public IHierarchicalItemModel? GetNextItem()
    {
        // If this node is open and has children, descend into the first child.
        if (IsOpened && Children.Any())
        {
            return Children.AsSnapshot().First();
        }

        // Otherwise walk up ancestors looking for the next sibling.
        IHierarchicalItemModel current = this;
        while (current.Parent is not null)
        {
            var siblings = current.Parent.Children.AsSnapshot();
            var index = siblings.IndexOf(current) + 1;
            if (index < siblings.Count)
            {
                return siblings[index];
            }
            current = current.Parent;
        }

        // Root node with no open children – stay put.
        return this;
    }

    public IHierarchicalItemModel? GetPreviousItem()
    {
        if (Parent is not null)
        {
            var siblings = Parent.Children.AsSnapshot();
            var index = siblings.IndexOf((IHierarchicalItemModel)this) - 1;
            if (index >= 0)
            {
                // Navigate to the last visible descendant of the previous sibling.
                return GetLastVisibleDescendant(siblings[index]);
            }
            else
            {
                return Parent;
            }
        }
        // Root node – stay put.
        return this;
    }

    private static IHierarchicalItemModel GetLastVisibleDescendant(IHierarchicalItemModel item)
    {
        if (item.IsOpened && item.Children.Any())
        {
            return GetLastVisibleDescendant(item.Children.AsSnapshot().Last());
        }
        return item;
    }


    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}
