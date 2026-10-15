namespace iConsole.Diagnostics;

/// <summary>
/// A text capture of a fully composed frame (a <see cref="Buffers.BufferLayer"/> merged from all
/// visible controls), recorded by whichever <see cref="IScreenLogger"/> is attached at the time
/// the frame was rendered.
/// </summary>
public sealed record ScreenSnapshot(
    DateTime Timestamp,
    string Label,
    string Text,
    int Width,
    int Height,
    IReadOnlyCollection<string> Details);
