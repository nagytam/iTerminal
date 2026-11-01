using iConsole.Data;
using iConsole.Helper;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iConsole.Model;

public class ScrollableModel : IColumnScrollableModel, INotifyPropertyChanged
{
    private int _columnCount = 1;
    public int ColumnCount
    {
        get
        {
            return _columnCount;
        }
        set
        {
            var columnCount = Math.Max(1, value);
            if (_columnCount != columnCount)
            {
                _columnCount = columnCount;
                OnPropertyChanged(nameof(ColumnCount));
            }
        }
    }

    #region Model
    private Position _cursorPosition = new(0, 0);
    public Position CursorPosition 
    { 
        get
        {
            return _cursorPosition;
        }
        set 
        {
            if (_cursorPosition != value)
            {
                if (_cursorPosition != null)
                {
                    _cursorPosition.PropertyChanged -= OnCursorPositionPropertyChanged;
                }
                _cursorPosition = value;
                if (_cursorPosition != null)
                {
                    _cursorPosition.PropertyChanged += OnCursorPositionPropertyChanged;
                }
                OnPropertyChanged(nameof(CursorPosition));
            }
        }
    }

    private Position _scrollPosition = new(0, 0);
    public Position ScrollPosition 
    {
        get 
        {
            return _scrollPosition;
        } 
        set
        {
            if (_scrollPosition != value)
            {
                if (_scrollPosition != null)
                {
                    _scrollPosition.PropertyChanged -= OnScrollPositionPropertyChanged;
                }
                _scrollPosition = value;
                if (_scrollPosition != null)
                {
                    _scrollPosition.PropertyChanged += OnScrollPositionPropertyChanged;
                }
                OnPropertyChanged(nameof(ScrollPosition));
            }
        }  
    }

    private Size _size = new(0, 0);
    public Size ViewSize
    {
        get
        {
            return _size;
        }
        set
        {
            if (_size != value)
            {
                _size = value;
                OnPropertyChanged(nameof(ViewSize));
            }
        }
    }

    private Size _scrollableSize = new(0, 0);
    public Size ScrollableSize
    {
        get
        {
            return _scrollableSize;
        }
        set
        {
            if (_scrollableSize != value)
            {
                _scrollableSize = value;
                OnPropertyChanged(nameof(ScrollableSize));
            }
        }
    }
    #endregion

    private void OnCursorPositionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CursorPosition));
    }

    private void OnScrollPositionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ScrollPosition));
    }

    /// <summary>
    /// Rounds the scroll position down to a line start. Writes only when the value changes.
    /// </summary>
    public void AlignScroll()
    {
        var aligned = ScrollPosition.Y - (ScrollPosition.Y % ColumnCount);
        if (aligned != ScrollPosition.Y)
        {
            ScrollPosition.Y = aligned;
        }
    }

    /// <summary>
    /// The vertical part of <see cref="ScrollToVisible"/>, in lines: keeps the cursor's line inside the
    /// visible lines. With one column this is the entry-by-entry scroll it always was.
    /// </summary>
    public void ScrollToVisibleVertical() => ScrollLinesToVisible(this);

    /// <summary>The number of columns a model lays out, 1 for any model without the column interface.</summary>
    public static int ColumnCountOf(IScrollableModel model) =>
        model is IColumnScrollableModel columns ? Math.Max(1, columns.ColumnCount) : 1;

    /// <summary>
    /// The vertical scroll of a model in lines of <see cref="ColumnCountOf"/> entries: keeps the cursor's
    /// line inside the visible lines. Works on any <see cref="IScrollableModel"/>; with one column it is
    /// the entry-by-entry scroll.
    /// </summary>
    public static void ScrollLinesToVisible(IScrollableModel model)
    {
        var columns = ColumnCountOf(model);
        var cursorLine = model.CursorPosition.Y / columns;
        var firstLine = model.ScrollPosition.Y / columns;
        var rows = model.ViewSize.Height;
        if (cursorLine < firstLine)
        {
            model.ScrollPosition.Y = cursorLine * columns;
        }
        else if (cursorLine > firstLine + rows - 1)
        {
            model.ScrollPosition.Y = Math.Max(0, cursorLine - rows + 1) * columns;
        }
    }

    public void ScrollToVisible()
    {
        ScrollToVisibleVertical();
        if (CursorPosition.X < ScrollPosition.X)
        {
            ScrollPosition.X = CursorPosition.X;
        }
        else if (CursorPosition.X > ScrollPosition.X + ViewSize.Width - 1)
        {
            ScrollPosition.X = Math.Max(0, CursorPosition.X - ViewSize.Width + 1);
        }
    }

    public virtual void Reset()
    {
        ScrollPosition.Reset();
        CursorPosition.Reset();
        ViewSize.Reset();
    }

    public virtual void Update()
    {
        OnPropertyChanged();
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion

    public ScrollableModel()
    {
        _cursorPosition.PropertyChanged += OnCursorPositionPropertyChanged;
        _scrollPosition.PropertyChanged += OnScrollPositionPropertyChanged;
    }
}
