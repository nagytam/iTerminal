namespace iConsole.Data;

public class ObservableCollection<T> : System.Collections.ObjectModel.ObservableCollection<T>
{
    public void ReplaceAt(int index, T item)
    {
        SetItem(index, item);
    }
}
