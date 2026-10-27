namespace iConsole.Inputs;

/// <summary>
/// Tells a key that is held down from one that was just pressed. The Windows console reports every auto-repeat as another
/// key-down record, with nothing to say it is a repeat; the only sign is that the key went down again without going up. This
/// remembers which keys are down, so the second and later key-downs of the same key are repeats. Not thread safe: one input
/// thread owns it.
/// </summary>
public sealed class KeyRepeatTracker
{
    /// <summary>The most repeats one record may stand for. A record that says more is held to this.</summary>
    public const int MaxRepeatCount = 64;

    private readonly HashSet<int> _down = [];

    /// <summary>
    /// Records a key-down or key-up of <paramref name="virtualKey"/>. Returns true for a key-down of a key that was already
    /// down, and false for a first press and for every release.
    /// </summary>
    public bool Register(int virtualKey, bool keyDown)
    {
        if (!keyDown)
        {
            _down.Remove(virtualKey);
            return false;
        }

        return !_down.Add(virtualKey);
    }

    /// <summary>Forgets every key; the window lost the focus, so releases may have gone to another window.</summary>
    public void Reset() => _down.Clear();

    /// <summary>How many events one record stands for: its repeat count, at least one and no more than <see cref="MaxRepeatCount"/>.</summary>
    public static int CountFor(int repeatCount) => Math.Clamp(repeatCount, 1, MaxRepeatCount);
}
