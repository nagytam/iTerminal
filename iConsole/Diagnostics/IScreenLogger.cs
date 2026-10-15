using iConsole.Buffers;

namespace iConsole.Diagnostics;

/// <summary>
/// Observes fully composed frames as they are merged during rendering. Production wiring calls
/// this from <see cref="Jobs.ControlRendererJob"/> after each buffer merge; tests can also call
/// it directly against a <see cref="Test.Core.MockBuffer"/>'s <c>MergedLayer</c> after driving a
/// key event, to record what the screen looked like at that point.
/// </summary>
public interface IScreenLogger
{
    void Capture(string label, BufferLayer layer, IReadOnlyCollection<string>? details = null);
}
