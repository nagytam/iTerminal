using iConsole.Data;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace iConsole.Model;

public class TextModel : ITextModel, INotifyPropertyChanged
{
    public ConcurrentList<string> Lines { get; set; } = [];
    public Encoding Encoding { get; set; } = Encoding.UTF8;

    public TextModel()
    {
    }

    public TextModel(string text)
    {
        Lines = [text];
    }

    public void TriggerPropertyChanged()
    {
        OnPropertyChanged(nameof(Lines));
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}
