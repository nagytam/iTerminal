using iConsole.Display;

namespace iConsole.Model;

/// <summary>
/// A mark on a line or a stretch of text in the viewer or the editor: a diagnostic's underline, a diff's green and red lines,
/// the line of a stack trace, a blame colour. Lines and columns count from 0 and columns are characters of the text, not cells.
/// </summary>
/// <param name="Line">The text line.</param>
/// <param name="Column">The first column marked.</param>
/// <param name="Length">How many characters are marked; a negative length marks to the end of the line and on across the rest of the row, so a background fills it.</param>
/// <param name="Source">Who set the mark (<c>"lsp"</c>, <c>"diff"</c>); a source can replace or remove all of its marks at once.</param>
/// <param name="Foreground">Text colour, or null to leave it. Not applied on the cursor line, which has its own colours.</param>
/// <param name="Background">Background colour, or null to leave it. Not applied on the cursor line.</param>
/// <param name="Underline">The underline style, or null for none.</param>
/// <param name="UnderlineColor">The underline's own colour; null follows the text.</param>
/// <param name="Bold">Bold text.</param>
/// <param name="Italic">Italic text.</param>
/// <param name="Dim">Faint text.</param>
/// <param name="Message">What to tell the user about the mark, such as a diagnostic's text; found with <c>MessageAt</c>.</param>
/// <param name="Priority">Marks are applied from the lowest priority up, so a higher one wins where they overlap.</param>
public sealed record TextDecoration(
    int Line,
    int Column,
    int Length,
    string Source = "",
    Color? Foreground = null,
    Color? Background = null,
    UnderlineStyle? Underline = null,
    Color? UnderlineColor = null,
    bool Bold = false,
    bool Italic = false,
    bool Dim = false,
    string? Message = null,
    int Priority = 0)
{
    /// <summary>One past the last column marked; <see cref="int.MaxValue"/> for a mark to the end of the line.</summary>
    public int End => Length < 0 ? int.MaxValue : Column + Length;

    /// <summary>True when the mark covers <paramref name="column"/> of its line.</summary>
    public bool Covers(int column) => column >= Column && column < End;

    /// <summary>A mark across the whole of a line.</summary>
    public static TextDecoration WholeLine(int line, string source = "", Color? foreground = null, Color? background = null, string? message = null, int priority = 0) =>
        new(line, 0, -1, source, foreground, background, Message: message, Priority: priority);
}

/// <summary>
/// The marks on a text. Safe to change from any thread (a language server's answer arrives on a worker): the control reads
/// a snapshot of one line at a time when it draws, and <see cref="Version"/> moves on every change so a control can tell it
/// has to repaint. Marks are kept by line number, so a source that marks text which then moves, such as a language server,
/// sets its marks again when the text changes.
/// </summary>
public sealed class DecorationSet
{
    private readonly Lock _gate = new();
    private readonly Dictionary<int, List<TextDecoration>> _byLine = [];
    private long _version;

    /// <summary>Moves on every change.</summary>
    public long Version => Interlocked.Read(ref _version);

    public int Count
    {
        get { lock (_gate) { return _byLine.Values.Sum(list => list.Count); } }
    }

    public void Add(TextDecoration decoration)
    {
        ArgumentNullException.ThrowIfNull(decoration);
        lock (_gate)
        {
            Insert(decoration);
        }

        Interlocked.Increment(ref _version);
    }

    public void AddRange(IEnumerable<TextDecoration> decorations)
    {
        ArgumentNullException.ThrowIfNull(decorations);
        lock (_gate)
        {
            foreach (TextDecoration decoration in decorations)
            {
                Insert(decoration);
            }
        }

        Interlocked.Increment(ref _version);
    }

    /// <summary>Takes away every mark <paramref name="source"/> set and puts <paramref name="decorations"/> in their place, in one step.</summary>
    public void ReplaceSource(string source, IEnumerable<TextDecoration> decorations)
    {
        ArgumentNullException.ThrowIfNull(decorations);
        lock (_gate)
        {
            RemoveSource(source);
            foreach (TextDecoration decoration in decorations)
            {
                Insert(decoration with { Source = source });
            }
        }

        Interlocked.Increment(ref _version);
    }

    /// <summary>Takes away every mark <paramref name="source"/> set. Returns whether there were any.</summary>
    public bool Remove(string source)
    {
        bool removed;
        lock (_gate)
        {
            removed = RemoveSource(source);
        }

        if (removed)
        {
            Interlocked.Increment(ref _version);
        }

        return removed;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _byLine.Clear();
        }

        Interlocked.Increment(ref _version);
    }

    /// <summary>The marks on <paramref name="line"/>, lowest priority first.</summary>
    public IReadOnlyList<TextDecoration> ForLine(int line)
    {
        lock (_gate)
        {
            return _byLine.TryGetValue(line, out List<TextDecoration>? list) ? [.. list.OrderBy(d => d.Priority)] : [];
        }
    }

    /// <summary>The highest-priority mark at <paramref name="column"/> of <paramref name="line"/> that has a message.</summary>
    public TextDecoration? MessageAt(int line, int column)
    {
        lock (_gate)
        {
            return _byLine.TryGetValue(line, out List<TextDecoration>? list)
                ? list.Where(d => d.Message is not null && d.Covers(column)).OrderByDescending(d => d.Priority).FirstOrDefault()
                : null;
        }
    }

    /// <summary>Every mark, ordered by line, column and priority.</summary>
    public IReadOnlyList<TextDecoration> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _byLine.OrderBy(pair => pair.Key).SelectMany(pair => pair.Value.OrderBy(d => d.Column).ThenBy(d => d.Priority))];
            }
        }
    }

    private void Insert(TextDecoration decoration)
    {
        if (decoration.Line < 0 || decoration.Column < 0 || decoration.Length == 0)
        {
            return;
        }

        if (!_byLine.TryGetValue(decoration.Line, out List<TextDecoration>? list))
        {
            list = [];
            _byLine[decoration.Line] = list;
        }

        list.Add(decoration);
    }

    private bool RemoveSource(string source)
    {
        bool removed = false;
        foreach (int line in _byLine.Keys.ToList())
        {
            int count = _byLine[line].RemoveAll(d => d.Source == source);
            removed |= count > 0;
            if (_byLine[line].Count == 0)
            {
                _byLine.Remove(line);
            }
        }

        return removed;
    }
}
