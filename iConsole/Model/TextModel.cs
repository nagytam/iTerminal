using iConsole.Data;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace iConsole.Model;

public class TextModel : ITextModel, INotifyPropertyChanged
{
    public ConcurrentList<string> Lines { get; set; } = [];

    private Encoding _encoding = Encoding.UTF8;

    public Encoding Encoding
    {
        get => _encoding;
        set
        {
            if (!ReferenceEquals(_encoding, value))
            {
                _encoding = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The break the text was read with (see <see cref="Helper.LineEndings"/>); saving writes it back.</summary>
    public string LineEnding { get; set; } = Environment.NewLine;

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
