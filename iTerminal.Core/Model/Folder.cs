using iConsole.Data;
using iConsole.Model;

namespace iTerminal.Core.Model;

public class Folder : HierarchicalItemModel
{
    public Url? Url { get; private set; } = null;

    public Folder(Folder? parent, string folderName) : base(parent, folderName)
    {
        if (folderName != "My Computer")
        {
            Url = new Url(folderName);
        }
    }
}
