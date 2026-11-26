using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;

namespace iTerminal.Core.Dialogs.Dotnet;

/// <summary>
/// A scrolling list of lines in the solution viewer's right column. The Output pane holds the whole
/// log of a run; the Error pane holds only its error lines, and works the same way. While the user
/// has not scrolled, the pane keeps the last line in view; a scroll turns that off until End.
/// </summary>
internal sealed class SolutionOutputPane
{
    /// <summary>Colours the lines the way the terminal reads them: commands, errors, warnings and results.</summary>
    internal sealed class OutputListBox(IListBoxModel<string> listModel, IThemeService themeService)
        : ListBox<string>(listModel, themeService)
    {
        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            var line = ListModel.ListItems[index].Item;
            if (line.StartsWith("$ ", StringComparison.Ordinal)) return Color.Cyan;
            if (line.Contains(": error", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Error", StringComparison.Ordinal)
                || line.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Failed!", StringComparison.Ordinal)) return Color.Red;
            if (line.Contains(": warning", StringComparison.OrdinalIgnoreCase)) return Color.Yellow;
            if (line.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Passed!", StringComparison.Ordinal)) return Color.Green;
            return null;
        }
    }

    private readonly ConcurrentObservableCollection<string> _items = new();
    private readonly Lock _lock = new();
    private readonly List<string> _lines = [];
    private bool _followTail = true;
    private bool _syncingScroll;

    public SolutionOutputPane(IThemeService themeService)
    {
        ListBox = new OutputListBox(
            new ListBoxModel<string>(_items) { TransformItem = TransformLine },
            themeService)
        {
            CanFocus = false,
        };

        ListBox.ScrollableModel.PropertyChanged += (_, e) =>
        {
            if (!_syncingScroll && e.PropertyName == nameof(IScrollableModel.CursorPosition))
            {
                _followTail = false;
            }
        };
    }

    /// <summary>The list the dialog adds to its control tree and lays out.</summary>
    public OutputListBox ListBox { get; }

    /// <summary>True while the pane keeps the last line in view.</summary>
    public bool IsFollowingTail => _followTail;

    /// <summary>A snapshot of the lines, in order.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lock)
            {
                return [.. _lines];
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _lines.Count;
            }
        }
    }

    private static ListItem<string> TransformLine(string line, CancellationToken cancellationToken) =>
        new(line) { Value = line, IsEnabled = true };

    /// <summary>Empties the pane and starts following the tail again.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _lines.Clear();
        }

        _followTail = true;
        SyncItems();
    }

    public void Append(string line)
    {
        lock (_lock)
        {
            _lines.Add(line);
        }

        SyncItems();
    }

    /// <summary>Scrolls by <paramref name="delta"/> lines (PageUp/PageDown use 10). Stops following the tail.</summary>
    public void Scroll(int delta)
    {
        _followTail = false;
        var rows = (int)ListBox.ViewSize.Height;
        var count = ListBox.ListModel.ListItems.Count;
        _syncingScroll = true;
        try
        {
            var newScroll = Math.Clamp((int)ListBox.ScrollableModel.ScrollPosition.Y + delta, 0, Math.Max(0, count - rows));
            var newCursor = Math.Clamp((int)ListBox.ScrollableModel.CursorPosition.Y + delta, 0, Math.Max(0, count - 1));
            ListBox.ScrollableModel.ScrollPosition.Y = newScroll;
            ListBox.ScrollableModel.CursorPosition.Y = newCursor;
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    /// <summary>Home: the first line. Stops following the tail.</summary>
    public void ScrollToTop()
    {
        _followTail = false;
        _syncingScroll = true;
        try
        {
            ListBox.ScrollableModel.ScrollPosition.Y = 0;
            ListBox.ScrollableModel.CursorPosition.Y = 0;
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    /// <summary>End: follows the tail again and shows the last line.</summary>
    public void FollowTail()
    {
        _followTail = true;
        ResyncTailScroll();
    }

    /// <summary>Pushes the lines into the list's model and keeps the tail in view while following.</summary>
    private void SyncItems()
    {
        string[] lines;
        lock (_lock)
        {
            lines = [.. _lines];
        }

        _items.ReplaceAll(lines);
        ListBox.UpdateModel();
        ResyncTailScroll();
    }

    /// <summary>Called by the dialog after its layout changes the list's size, so the tail stays in view.</summary>
    public void ResyncTailScroll()
    {
        if (!_followTail)
        {
            return;
        }

        _syncingScroll = true;
        try
        {
            var count = ListBox.ListModel.ListItems.Count;
            var rows = (int)ListBox.ViewSize.Height;
            ListBox.ScrollableModel.ScrollPosition.Y = Math.Max(0, count - rows);
            ListBox.ScrollableModel.CursorPosition.Y = Math.Max(0, count - 1);
        }
        finally
        {
            _syncingScroll = false;
        }
    }
}
