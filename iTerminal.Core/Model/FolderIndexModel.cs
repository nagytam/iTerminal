using iTerminal.Core.Services.BackgroundStart;

namespace iTerminal.Core.Model;

public class FolderIndexModel : Folder
{
    private readonly IBackgroundStartService? _backgroundStart;

    public FolderIndexModel(IBackgroundStartService? backgroundStart = null) : base(null, "My Computer")
    {
        _backgroundStart = backgroundStart;
        IsOpened = true;
        IsFocused = true;
    }

    /// <summary>
    /// Asks for the whole-disk index to be built. It is read only by the folder selector, so it is walked on demand:
    /// the request starts it, unless the folder index is configured to start sooner. Idempotent.
    /// </summary>
    public void RequestIndexing() => _backgroundStart?.Request(BackgroundServices.FolderIndex);
}
