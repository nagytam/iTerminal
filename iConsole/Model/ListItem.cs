namespace iConsole.Model;

public class ListItem<T> : IListItem<T>
{
    public T Item { get; private set; }
    public string Value { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsSelected { get; set; }

    public ListItem(T? item)
    {
        Item = item!;
        Value = Item?.ToString() ?? string.Empty;
    }
}