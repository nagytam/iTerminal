using iConsole.Data;
using System.ComponentModel;

namespace iConsole.Model;

public interface IHierarchicalItemModel : INotifyPropertyChanged
{
    string Name { get; }
    ConcurrentObservableCollection<IHierarchicalItemModel> Children { get; set; } 
    Position Position { get; set; }
    Size Size { get; set; }
    int Count { get; }
    bool IsOpened { get; set; }
    bool IsFocused { get; set; }
    IHierarchicalItemModel? Parent { get; }
    IHierarchicalItemModel? GetFocusedItem();
    IHierarchicalItemModel? GetNextItem();
    IHierarchicalItemModel? GetPreviousItem();
    void OnHierarchyChanged();
}