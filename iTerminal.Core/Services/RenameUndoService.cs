using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services;

/// <summary>
/// Remembers the last rename made from the Rename dialog, so Ctrl+Z can put the entry back. One step only: a second
/// rename replaces the first, and the undo is forgotten once it has been used.
/// </summary>
public sealed class RenameUndoService
{
    public sealed record Rename(IContentModel Model, Url Source, Url Target);

    private readonly object _lock = new();
    private Rename? _last;

    public void Remember(IContentModel model, Url source, Url target)
    {
        lock (_lock)
        {
            _last = new Rename(model, source, target);
        }
    }

    /// <summary>The rename that Ctrl+Z would undo, or null when there is none.</summary>
    public Rename? Peek()
    {
        lock (_lock)
        {
            return _last;
        }
    }

    /// <summary>Takes the rename out of the service, so it can be undone once.</summary>
    public Rename? Take()
    {
        lock (_lock)
        {
            var last = _last;
            _last = null;
            return last;
        }
    }
}
