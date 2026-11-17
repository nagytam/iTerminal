using iConsole.Data;

namespace iConsole.Viewers;

/// <summary>
/// The handlers of one kind (viewers or editors) that the container knows, in the order they are
/// offered a file: <see cref="IFileHandler.Priority"/> descending, registration order within a priority.
/// </summary>
public abstract class FileHandlerRegistry<THandler>(IEnumerable<THandler> handlers)
    where THandler : IFileHandler
{
    // OrderByDescending is stable, so equal priorities keep the order the container listed them in.
    private readonly IReadOnlyList<THandler> _handlers = handlers.OrderByDescending(handler => handler.Priority).ToList();

    public IReadOnlyList<THandler> Handlers => _handlers;

    /// <summary>The first handler, in offer order, that takes the file by its name; null when none does.</summary>
    public THandler? FirstCandidate(Url url) => _handlers.FirstOrDefault(handler => handler.IsCandidate(url));

    public bool HasCandidate(Url url) => FirstCandidate(url) is not null;
}

/// <summary>Every <see cref="IFileViewer"/> the container knows.</summary>
public sealed class FileViewerRegistry(IEnumerable<IFileViewer> viewers) : FileHandlerRegistry<IFileViewer>(viewers);

/// <summary>Every <see cref="IFileEditor"/> the container knows.</summary>
public sealed class FileEditorRegistry(IEnumerable<IFileEditor> editors) : FileHandlerRegistry<IFileEditor>(editors);
