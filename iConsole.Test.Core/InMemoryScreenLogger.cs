using iConsole.Buffers;
using iConsole.Diagnostics;

namespace iConsole.Test.Core;

/// <summary>
/// IScreenLogger that records a text snapshot of every merged frame in memory so agentic/
/// integration tests can assert on what the screen looked like after driving the app via key
/// events. Wire it into a real <see cref="iConsole.Desktops.Desktop"/> to capture every render
/// pass, or call <see cref="Capture"/> directly against a <see cref="MockBuffer"/>'s
/// <c>MergedLayer</c> after rendering a control tree by hand.
/// </summary>
public sealed class InMemoryScreenLogger : IScreenLogger
{
    private readonly List<ScreenSnapshot> _snapshots = [];

    public IReadOnlyList<ScreenSnapshot> Snapshots
    {
        get { lock (_snapshots) { return _snapshots.ToArray(); } }
    }

    public ScreenSnapshot? Last => Snapshots.LastOrDefault();

    public void Capture(string label, BufferLayer layer, IReadOnlyCollection<string>? details = null)
    {
        var snapshot = new ScreenSnapshot(
            DateTime.Now,
            label,
            layer.AsString(),
            (int)layer.Size.Width,
            (int)layer.Size.Height,
            details ?? []);
        lock (_snapshots) { _snapshots.Add(snapshot); }
    }

    public void Clear()
    {
        lock (_snapshots) { _snapshots.Clear(); }
    }
}
