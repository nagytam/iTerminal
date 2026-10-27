using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace iConsole.Data;

[DebuggerDisplay("({X}, {Y})")]
public record class Position : INotifyPropertyChanged
{
    private long _x = 0;
    private long _y = 0;
    public long X 
    {
        get 
        { 
            return _x; 
        } 
        set
        {
            // A position worked out from a smaller parent can come to a negative one; the screen starts at 0.
            value = Math.Max(0, value);
            if (value != _x)
            {
                Debug.Assert(value >= 0);
                _x = value;
                OnPropertyChanged(nameof(X));
            }
        }
    }
    public long Y 
    {
        get
        {
            return _y;
        }
        set
        {
            value = Math.Max(0, value);
            if (value != _y)
            {
                Debug.Assert(value >= 0);
                _y = value;
                OnPropertyChanged(nameof(Y));
            }
        }
    }
    public Position(long x, long y)
    {
        X = x;
        Y = y;
    }

    public static Position operator +(Position? a, Position b)
    {
        if (a != null)
        {
            return new Position(a.X + b.X, a.Y + b.Y);
        }
        else
        {
            return new Position(b.X, b.Y);
        }
    }

    public void Reset()
    {
        X = 0;
        Y = 0;
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}
