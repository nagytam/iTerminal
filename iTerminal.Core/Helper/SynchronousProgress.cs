namespace iTerminal.Core.Helper;

/// <summary>
/// Invokes the callback synchronously on the reporting thread. <see cref="Progress{T}"/> instead
/// marshals through the captured <see cref="System.Threading.SynchronizationContext"/> (or the
/// thread pool when none is captured), which can deliver a report *after* code that runs
/// immediately following the operation that produced it — e.g. a final 100% report arriving after
/// the caller already moved on, silently overwriting a later "done" state with a stale value.
/// </summary>
public sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
