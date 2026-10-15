namespace iConsole.Data;

public record KeyEvent : Key
{
    public char KeyChar { get; private init; } = ' ';
    public KeyPressType KeyPressType { get; private init; }

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
