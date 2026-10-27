using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace iConsole.Data;

[DebuggerDisplay("[{Width}, {Height}]")]
public record class Size : INotifyPropertyChanged
{
    private long _width = 0;
    public long Width 
    { 
        get
        {
            return _width;
        }
        set
        {
            // A layout that works a size out of a smaller parent can come to a negative one; nothing is negative, so it is empty.
            value = Math.Max(0, value);
            if (value != _width)
            {
                Debug.Assert(value >= 0);
                Debug.Assert(value <= 1000000);
                _width = value;
                OnPropertyChanged(nameof(Width));
            }
        }
    }

    private long _height = 0;

    public long Height 
    { 
        get
        {
            return _height;
        }
        set
        {
            value = Math.Max(0, value);
            if (_height != value)
            {
                Debug.Assert(value >= 0);
                Debug.Assert(value <= 1000000);
                _height = value;
                OnPropertyChanged(nameof(Height));
            }
        }
    }
    public bool IsEmpty => (Width==0) || (Height==0);

    public Size(long width, long height)
    {
        Width = width;
        Height = height;
    }
    
    public Size(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public void Intersect(Size size)
    {
        Width = Math.Min(Width, size.Width);
        Height = Math.Min(Height, size.Height);
    }

    public static Size operator *(Size size, double factor)
    {
        Size newSize = new(
        (long)(size.Width * factor),
        (long)(size.Height * factor));
        return newSize;
    }

    public void Reset()
    {
        Width = 0;
        Height = 0;
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}
