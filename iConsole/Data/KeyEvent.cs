namespace iConsole.Data;

public record KeyEvent : Key
{
    public char KeyChar { get; private init; } = ' ';
    public KeyPressType KeyPressType { get; private init; }

    /// <summary>True for a key that is held down and repeating (the second and later key-downs of one press), where the input source can tell.</summary>
    public bool IsRepeat { get; init; }

    public KeyEvent(char keyChar, ConsoleKey consoleKey, bool shift, bool alt, bool control, KeyPressType keyPressType = KeyPressType.Down)
        : base(consoleKey, shift, alt, control)
    {
        KeyChar = keyChar;
        KeyPressType = keyPressType;
    }

    public override bool IsIdentical(KeyEvent keyEvent)
    {
        return KeyChar == keyEvent.KeyChar && base.IsIdentical(keyEvent);
    }
}
