using iConsole.Data;
using System.ComponentModel;
using System.Text;

namespace iConsole.Model;

public interface ITextModel : INotifyPropertyChanged
{
    ConcurrentList<string> Lines { get; set; }
    Encoding Encoding { get; set; }

    /// <summary>
    /// The line break the text came with, and is written back with. The default is the system's, which is what a text
    /// that was not read from a file (so has no break of its own) should get.
    /// </summary>
    string LineEnding
    {
        get => Environment.NewLine;
        set { }
    }

    void TriggerPropertyChanged();
}