using iConsole.Display;

namespace iConsole.Jobs;

public sealed record ConsoleDisplayWrite(int X, int Y, string Text, Color Foreground, Color Background, string Mode)
{
    public override string ToString()
    {
        return $"mode={Mode};x={X};y={Y};len={Text.Length};fg={Foreground};bg={Background};text=|{Text}|";
    }
}
