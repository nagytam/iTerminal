namespace iTerminal.DevelopmentStudio.Services;

/// <summary>
/// The Main program of the Solution Explorer: the item F5 and Ctrl+F5 start, whatever the cursor is on. It is a singleton, so
/// it survives closing the studio, as the breakpoints do.
/// </summary>
public sealed class MainProgramStore
{
    private readonly Lock _lock = new();
    private string _path = string.Empty;

    /// <summary>The path of the Main program, or empty when none is set.</summary>
    public string Path
    {
        get
        {
            lock (_lock) return _path;
        }
    }

    /// <summary>Makes the item at the path the Main program. Its path is not checked here; the explorer ignores a path it does not list.</summary>
    public void Set(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        lock (_lock) _path = path;
    }

    /// <summary>Clears the Main program.</summary>
    public void Clear()
    {
        lock (_lock) _path = string.Empty;
    }
}
