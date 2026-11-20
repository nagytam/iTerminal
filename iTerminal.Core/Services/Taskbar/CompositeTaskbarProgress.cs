namespace iTerminal.Core.Services.Taskbar;

/// <summary>
/// Shows the indicator on several places at once, for example the Windows taskbar button and the terminal's own tab. Each
/// inner implementation is called on its own: a failing one is logged to the debug output and does not stop the others, as
/// <see cref="TaskbarProgressJob"/> does for the indicator as a whole. Disposing it disposes the inner implementations that
/// are disposable, so the Windows worker thread is still released at shutdown.
/// </summary>
public sealed class CompositeTaskbarProgress(params ITaskbarProgress[] inner) : ITaskbarProgress, IDisposable
{
    public void Update(TaskbarState state, long percentage)
    {
        foreach (ITaskbarProgress progress in inner)
        {
            try
            {
                progress.Update(state, percentage);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Taskbar progress ({progress.GetType().Name}): {exception.Message}");
            }
        }
    }

    public void Dispose()
    {
        foreach (IDisposable disposable in inner.OfType<IDisposable>())
        {
            disposable.Dispose();
        }
    }
}
