using iConsole.Data;
using System.ComponentModel;
using System.Text;

namespace iConsole.Model;

public interface ITextModel : INotifyPropertyChanged
{
    ConcurrentList<string> Lines { get; set; }
    Encoding Encoding { get; set; }

    void TriggerPropertyChanged();
}