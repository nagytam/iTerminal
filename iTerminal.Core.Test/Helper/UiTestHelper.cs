using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Test.Core;

namespace iTerminal.Core.Test.Helper;

/// <summary>
/// Shared plumbing for the dialog and panel tests: show a dialog the way the app does (attached to a
/// root control of a real screen size), press keys, wait for background work, and read back what the
/// whole control tree drew — the dialog and every visible child, since what matters to the user is
/// the composed screen and not one control's layer.
/// </summary>
internal static class UiTestHelper
{
    public static readonly Size ScreenSize = new(120, 40);

    public static IThemeService TestTheme() => new TestThemeService();

    public static async Task<Control> ShowAsync(Dialog dialog, Size? screenSize = null)
    {
        var root = new Control(TestTheme()) { Name = "Root", ViewSize = screenSize ?? ScreenSize };
        await root.ShowDialogAsync(dialog);
        return root;
    }

    public static KeyEvent Key(ConsoleKey consoleKey, char keyChar = '\0', bool shift = false, bool alt = false, bool control = false) =>
        new(keyChar, consoleKey, shift, alt, control);

    public static KeyEvent Char(char keyChar) =>
        new(keyChar, CharToConsoleKey(keyChar), char.IsUpper(keyChar), false, false);

    private static ConsoleKey CharToConsoleKey(char keyChar)
    {
        char upper = char.ToUpperInvariant(keyChar);
        if (upper is >= 'A' and <= 'Z')
        {
            return ConsoleKey.A + (upper - 'A');
        }

        if (keyChar is >= '0' and <= '9')
        {
            return ConsoleKey.D0 + (keyChar - '0');
        }

        return keyChar switch
        {
            ' ' => ConsoleKey.Spacebar,
            '+' => ConsoleKey.Add,
            '-' => ConsoleKey.Subtract,
            '*' => ConsoleKey.Multiply,
            '/' => ConsoleKey.Divide,
            '.' => ConsoleKey.Decimal,
            _ => ConsoleKey.Oem1,
        };
    }

    public static async Task TypeAsync(IControl control, string text)
    {
        foreach (char c in text)
        {
            await control.HandleKeyEventAsync(Char(c));
        }
    }

    /// <summary>Polls <paramref name="condition"/> until it holds; fails the test with <paramref name="because"/> if it never does.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, string because, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting: {because}");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Draws <paramref name="control"/> and every visible descendant into a fresh screen.</summary>
    public static ScreenBuffer Render(IControl control, Size? screenSize = null)
    {
        var size = screenSize ?? ScreenSize;
        var buffer = new ScreenBuffer((int)size.Width, (int)size.Height);
        DrawTree(control, buffer);
        return buffer;
    }

    private static void DrawTree(IControl control, ScreenBuffer buffer)
    {
        if (!control.IsVisible)
        {
            return;
        }

        control.Draw(buffer);
        foreach (var child in control.Controls.AsSnapshot())
        {
            DrawTree(child, buffer);
        }
    }

    public static void AssertScreenContains(ScreenBuffer screen, string text, string? because = null) =>
        Assert.IsTrue(screen.Contains(text),
            $"{because ?? "expected the screen to contain"} '{text}'.{Environment.NewLine}Screen was:{Environment.NewLine}{screen}");

    public static void AssertScreenDoesNotContain(ScreenBuffer screen, string text, string? because = null) =>
        Assert.IsFalse(screen.Contains(text),
            $"{because ?? "expected the screen not to contain"} '{text}'.{Environment.NewLine}Screen was:{Environment.NewLine}{screen}");
}
