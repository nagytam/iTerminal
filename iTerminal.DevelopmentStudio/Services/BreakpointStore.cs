namespace iTerminal.DevelopmentStudio.Services;

/// <summary>
/// The breakpoints the user has set, by file and line (1-based). The debugger reads them when a session starts and sends
/// each file's lines to the adapter; toggling one while a session runs sends the file again.
/// </summary>
public sealed class BreakpointStore
{
    private readonly Dictionary<string, SortedSet<int>> _byFile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();

    /// <summary>Sets the breakpoint at the line if it is not set, and removes it if it is. Returns true when one now exists.</summary>
    public bool Toggle(string path, int line)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        lock (_lock)
        {
            if (!_byFile.TryGetValue(path, out SortedSet<int>? lines))
            {
                lines = [];
                _byFile[path] = lines;
            }

            if (lines.Remove(line))
            {
                if (lines.Count == 0) _byFile.Remove(path);
                return false;
            }

            lines.Add(line);
            return true;
        }
    }

    public bool Contains(string path, int line)
    {
        lock (_lock)
        {
            return _byFile.TryGetValue(path, out SortedSet<int>? lines) && lines.Contains(line);
        }
    }

    /// <summary>Every breakpoint, ordered by file then line.</summary>
    public IReadOnlyList<(string Path, int Line)> All()
    {
        lock (_lock)
        {
            return _byFile.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(pair => pair.Value.Select(line => (pair.Key, line)))
                .ToList();
        }
    }

    /// <summary>The lines of one file's breakpoints, in order (empty when there are none).</summary>
    public IReadOnlyList<int> LinesIn(string path)
    {
        lock (_lock)
        {
            return _byFile.TryGetValue(path, out SortedSet<int>? lines) ? [.. lines] : [];
        }
    }

    /// <summary>Every file that has a breakpoint.</summary>
    public IReadOnlyList<string> Files()
    {
        lock (_lock)
        {
            return [.. _byFile.Keys];
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _byFile.Clear();
        }
    }
}
