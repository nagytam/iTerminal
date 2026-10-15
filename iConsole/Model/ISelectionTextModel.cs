using iConsole.Data;

namespace iConsole.Model;

public interface ISelectionTextModel
{
    bool IsSelected { get; set; }
    Position? Start { get; set; }
    Position? End { get; set; }
}