namespace iConsole.Interfaces;

public interface IContentBrowser
{
    bool IsFocused { get; }
    string Name { get; }
    Task ChangeModelAsync(string name);
}