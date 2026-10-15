namespace iConsole.Model;

public interface IListItem<T>
{
    public T Item { get; }
    public bool IsEnabled { get; }
    public bool IsSelected { get; set; }
    string Value { get; }
}