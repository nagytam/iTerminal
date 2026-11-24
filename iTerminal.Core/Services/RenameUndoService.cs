using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services;

/// <summary>
/// Remembers the last rename made from the Rename dialog, or the last batch made from Multi-rename, so Ctrl+Z can put
/// the names back. One step only: a later rename replaces the earlier one, and the undo is forgotten once it has been used.
/// </summary>
public sealed class RenameUndoService
{
    public sealed record Rename(IContentModel Model, Url Source, Url Target);

    private readonly Lock _lock = new();
    private IReadOnlyList<Rename> _last = [];

    public void Remember(IContentModel model, Url source, Url target)
    {
        lock (_lock)
        {
            _last = [new Rename(model, source, target)];
        }
    }

    /// <summary>
    /// Remembers every rename of one Multi-rename run as a single step. The pairs are the planned ones, including any
    /// that failed: the undo checks each pair again before it moves anything, so a step that never ran is skipped.
    /// An empty list leaves what was remembered before untouched.
    /// </summary>
    public void RememberBatch(IContentModel model, IEnumerable<(Url Source, Url Target)> renames)
    {
        Rename[] batch = [.. renames.Select(rename => new Rename(model, rename.Source, rename.Target))];
        if (batch.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            _last = batch;
        }
    }

    /// <summary>The first rename that Ctrl+Z would undo, or null when there is none.</summary>
    public Rename? Peek()
    {
        lock (_lock)
        {
            return _last.Count > 0 ? _last[0] : null;
        }
    }

    /// <summary>Takes the rename out of the service, so it can be undone once. For a batch this is its first rename only; <see cref="TakeAll"/> takes the whole step.</summary>
    public Rename? Take()
    {
        lock (_lock)
        {
            var first = _last.Count > 0 ? _last[0] : null;
            _last = [];
            return first;
        }
    }

    /// <summary>Takes the whole remembered step (one rename, or a batch in the order it ran), so it can be undone once.</summary>
    public IReadOnlyList<Rename> TakeAll()
    {
        lock (_lock)
        {
            var all = _last;
            _last = [];
            return all;
        }
    }
}
