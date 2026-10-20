using iConsole.Data;
using iConsole.Helper;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace iConsole.Model;

public class ScrollableModel : IScrollableModel, INotifyPropertyChanged
{
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

    public void ScrollToVisible()
    {
        ScrollPosition.Y.Range(0, ScrollableSize.Height - 1);
        if (CursorPosition.Y < ScrollPosition.Y)
        {
            ScrollPosition.Y = CursorPosition.Y;
        }
        else if (CursorPosition.Y > ScrollPosition.Y + ViewSize.Height - 1)
        {
            ScrollPosition.Y = Math.Max(0, CursorPosition.Y - ViewSize.Height + 1);
        }
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
