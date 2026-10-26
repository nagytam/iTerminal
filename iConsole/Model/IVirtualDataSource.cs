namespace iConsole.Model;

/// <summary>
/// Rows for a virtualized control, read by index. The control asks only for the rows on screen, so the source can be
/// millions of rows long, or load them from somewhere slow, without the control ever holding or copying the whole list.
/// </summary>
/// <remarks>
/// <see cref="TryGet"/> is called from <c>Draw</c>, so it must return at once and never wait for I/O: a row that is not
/// there yet returns false, and the control draws a placeholder. <see cref="Prefetch"/> is how the control says which rows
/// it is about to need; a source that loads in the background starts that work there and raises <see cref="Changed"/> when
/// rows arrive, from whatever thread it likes. Both are safe to call from any thread.
/// </remarks>
public interface IVirtualDataSource<T>
{
    /// <summary>The number of rows, or what is known of it so far.</summary>
    int Count { get; }

    /// <summary>
    /// The row at <paramref name="index"/> if it is available right now. False for a row that is not loaded yet or is out
    /// of range; the control shows a placeholder.
    /// </summary>
    bool TryGet(int index, out T? item);

    /// <summary>The rows from <paramref name="start"/> for <paramref name="count"/> rows are about to be drawn: load them if they are not.</summary>
    void Prefetch(int start, int count);

    /// <summary>Raised when the rows or their count changed, so a control should repaint. May be raised on any thread.</summary>
    event EventHandler? Changed;
}
