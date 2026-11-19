using iConsole.Data;

namespace iConsole.Viewers;

/// <summary>
/// The handlers of one kind (viewers or editors) that the container knows, in the order they are
/// offered a file: <see cref="IFileHandler.Priority"/> descending, registration order within a priority.
/// Handlers can be added and removed while the application runs (the plug-in manager does that); a
/// reader always sees one consistent snapshot of the order, so an open-file pass never sees a half-changed list.
/// </summary>
public abstract class FileHandlerRegistry<THandler>
    where THandler : IFileHandler
{
    private readonly Lock _gate = new();
    private readonly List<THandler> _registered;
    private volatile IReadOnlyList<THandler> _handlers;

    protected FileHandlerRegistry(IEnumerable<THandler> handlers)
    {
        _registered = [.. handlers];
        _handlers = Order(_registered);
    }

    /// <summary>The handlers in offer order, as they were at the last change.</summary>
    public IReadOnlyList<THandler> Handlers => _handlers;

    /// <summary>The first handler, in offer order, that takes the file by its name; null when none does.</summary>
    public THandler? FirstCandidate(Url url) => _handlers.FirstOrDefault(handler => handler.IsCandidate(url));

    public bool HasCandidate(Url url) => FirstCandidate(url) is not null;

    /// <summary>Adds a handler, after the ones already registered at its priority.</summary>
    public void Add(THandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _registered.Add(handler);
            _handlers = Order(_registered);
        }
    }

    /// <summary>Removes a handler. Returns false when it was not registered.</summary>
    public bool Remove(THandler handler)
    {
        lock (_gate)
        {
            if (!_registered.Remove(handler))
            {
                return false;
            }

            _handlers = Order(_registered);
            return true;
        }
    }

    // OrderByDescending is stable, so equal priorities keep the order they were registered in.
    private static IReadOnlyList<THandler> Order(List<THandler> registered) =>
        registered.OrderByDescending(handler => handler.Priority).ToList();
}

/// <summary>Every <see cref="IFileViewer"/> the container knows, plus any loaded by the plug-in manager.</summary>
public sealed class FileViewerRegistry(IEnumerable<IFileViewer> viewers) : FileHandlerRegistry<IFileViewer>(viewers);

/// <summary>Every <see cref="IFileEditor"/> the container knows, plus any loaded by the plug-in manager.</summary>
public sealed class FileEditorRegistry(IEnumerable<IFileEditor> editors) : FileHandlerRegistry<IFileEditor>(editors);
