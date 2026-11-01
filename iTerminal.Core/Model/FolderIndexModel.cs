namespace iTerminal.Core.Model;

public class FolderIndexModel : Folder
{
    private readonly TaskCompletionSource _indexingRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public FolderIndexModel() : base(null, "My Computer")
    {
        IsOpened = true;
        IsFocused = true;
    }

    /// <summary>
    /// Asks for the whole-disk index to be built. It is only read by the folder selector, so nothing is walked until
    /// something opens it. Idempotent: the first call starts the walk, later calls do nothing.
    /// </summary>
    public void RequestIndexing() => _indexingRequested.TrySetResult();

    /// <summary>Completes once <see cref="RequestIndexing"/> has been called.</summary>
    internal Task IndexingRequested => _indexingRequested.Task;
}
