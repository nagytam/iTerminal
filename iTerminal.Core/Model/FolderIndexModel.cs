namespace iTerminal.Core.Model;

public class FolderIndexModel : Folder
{
    public FolderIndexModel() : base(null, "My Computer")
    {
        IsOpened = true;
        IsFocused = true;
    }
}
