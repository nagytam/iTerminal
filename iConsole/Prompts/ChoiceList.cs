namespace iConsole.Prompts;

/// <summary>
/// The state behind a choice prompt: which choices match the filter, where the highlight is, which choices are marked and
/// how the visible part scrolls. It has no drawing and no keys, so it is tested on its own.
/// </summary>
public sealed class ChoiceList<T>
{
    private readonly IReadOnlyList<T> _choices;
    private readonly Func<T, string> _label;
    private readonly List<int> _visible = [];
    private readonly HashSet<int> _marked = [];
    private int _top;

    public ChoiceList(IReadOnlyList<T> choices, Func<T, string> label)
    {
        _choices = choices;
        _label = label;
        Refilter();
    }

    /// <summary>The text typed so far. A choice is shown when its label contains it, ignoring case.</summary>
    public string Filter { get; private set; } = string.Empty;

    /// <summary>The highlighted position among the visible choices.</summary>
    public int Cursor { get; private set; }

    /// <summary>How many choices match the filter.</summary>
    public int VisibleCount => _visible.Count;

    /// <summary>The highlighted choice, or the default when nothing matches.</summary>
    public T? Current => _visible.Count == 0 ? default : _choices[_visible[Cursor]];

    /// <summary>The label of the choice shown at <paramref name="visibleIndex"/>.</summary>
    public string LabelAt(int visibleIndex) => _label(_choices[_visible[visibleIndex]]);

    /// <summary>Whether the choice shown at <paramref name="visibleIndex"/> is marked.</summary>
    public bool IsMarked(int visibleIndex) => _marked.Contains(_visible[visibleIndex]);

    /// <summary>The marked choices, in the order they were offered.</summary>
    public IReadOnlyList<T> MarkedItems => _marked.OrderBy(index => index).Select(index => _choices[index]).ToList();

    /// <summary>Moves the highlight by <paramref name="delta"/> rows, stopping at the first and last.</summary>
    public void Move(int delta) => Cursor = Math.Clamp(Cursor + delta, 0, Math.Max(0, _visible.Count - 1));

    /// <summary>Moves the highlight to the first (<c>0</c>) or last (<c>int.MaxValue</c>) visible choice.</summary>
    public void MoveTo(int position) => Cursor = Math.Clamp(position, 0, Math.Max(0, _visible.Count - 1));

    /// <summary>Adds a character to the filter.</summary>
    public void Type(char c)
    {
        Filter += c;
        Refilter();
    }

    /// <summary>Removes the last character of the filter, if there is one.</summary>
    public void Backspace()
    {
        if (Filter.Length == 0)
        {
            return;
        }

        Filter = Filter[..^1];
        Refilter();
    }

    /// <summary>Marks the highlighted choice, or unmarks it when it is already marked.</summary>
    public void ToggleCurrent()
    {
        if (_visible.Count == 0)
        {
            return;
        }

        var index = _visible[Cursor];
        if (!_marked.Add(index))
        {
            _marked.Remove(index);
        }
    }

    /// <summary>
    /// The first visible row to draw so the highlight stays on screen in a window of <paramref name="rows"/> rows. It moves only
    /// when the highlight would leave the window, so the list does not jump on every key.
    /// </summary>
    public int ScrollTop(int rows)
    {
        rows = Math.Max(1, rows);
        if (Cursor < _top)
        {
            _top = Cursor;
        }
        else if (Cursor >= _top + rows)
        {
            _top = Cursor - rows + 1;
        }
        _top = Math.Clamp(_top, 0, Math.Max(0, _visible.Count - rows));
        return _top;
    }

    private void Refilter()
    {
        _visible.Clear();
        for (var index = 0; index < _choices.Count; index++)
        {
            if (Filter.Length == 0 || _label(_choices[index]).Contains(Filter, StringComparison.OrdinalIgnoreCase))
            {
                _visible.Add(index);
            }
        }

        // Keep the highlight in range; the marks are kept by choice, so they survive the filter changing.
        Cursor = Math.Clamp(Cursor, 0, Math.Max(0, _visible.Count - 1));
    }
}
