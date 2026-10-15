namespace iConsole.Data;

public record Key
{
    public ConsoleKey ConsoleKey { get; private init; }
    public bool Shift { get; private init; }
    public bool Control { get; private init; }
    public bool Alt { get; private init; }

    public Key(ConsoleKey consoleKey, bool shift, bool alt, bool control)
    {
        ConsoleKey = consoleKey;
        Shift = shift;
        Control = control;
        Alt = alt;
    }

    public virtual bool IsIdentical(KeyEvent keyEvent)
    {
        return ConsoleKey == keyEvent.ConsoleKey
                && Shift == keyEvent.Shift
                && Control == keyEvent.Control 
                && Alt == keyEvent.Alt;
    }
}
