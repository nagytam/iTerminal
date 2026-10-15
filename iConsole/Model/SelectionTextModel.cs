using iConsole.Data;

namespace iConsole.Model;

public class SelectionTextModel : ISelectionTextModel
{
    public Position? Start { get; set; } = null;

    public Position? End { get; set; } = null;

    public bool IsSelected { get; set; }
}
