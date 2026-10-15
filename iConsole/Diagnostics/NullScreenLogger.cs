using iConsole.Buffers;

namespace iConsole.Diagnostics;

/// <summary>
/// No-op implementation of <see cref="IScreenLogger"/> used when no logger has been injected
/// (the production default). Allocation-free.
/// </summary>
public sealed class NullScreenLogger : IScreenLogger
{
    public static readonly NullScreenLogger Instance = new();

    private NullScreenLogger()
    {
    }

    public void Capture(string label, BufferLayer layer, IReadOnlyCollection<string>? details = null)
    {
    }
}
