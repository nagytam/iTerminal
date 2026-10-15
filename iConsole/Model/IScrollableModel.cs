using iConsole.Data;
using System.ComponentModel;

namespace iConsole.Model;

public interface IScrollableModel : INotifyPropertyChanged
{
    Position CursorPosition { get; set; }
    Position ScrollPosition { get; set; }
    Size ScrollableSize { get; set; }
    Size ViewSize { get; set; }

    void Reset();
    void ScrollToVisible();
    void Update();
}