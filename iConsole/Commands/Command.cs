using iConsole.Controls;
using iConsole.Data;
using iConsole.Diagnostics;
using iConsole.Dialogs;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace iConsole.Commands;

public abstract class Command : ICommand
{
    public virtual string Title => CreateDisplayTextFromTypeName(GetType().Name, nameof(Command));
    public virtual string Description => Title;
    public virtual string KeyDisplayText => FormatKey(Key);
    public IControl? Control { get; set; }
    public virtual Key? Key { get; init; }
    public ICommandLogger CommandLogger { get; set; } = NullCommandLogger.Instance;
    protected bool IsReadOnlyControl
    {
        get
        {
            var control = Control as IReadOnly;
            if (control is not null && control.IsReadOnly)
            {
                return true;
            }
            return false;
        }
    }

    public virtual bool CanExecute()
    {
        return true;
    }

    /// <summary>
    /// Closes the nearest enclosing <see cref="Dialog"/> of this command's control, if any.
    /// Prefer this over counted <c>Parent?.Parent?...Close()</c> chains, which silently break
    /// when a wrapper control is inserted between the command's control and the dialog.
    /// </summary>
    protected void CloseContainingDialog()
    {
        var control = Control;
        while (control is not null && control is not Dialog)
        {
            control = control.Parent;
        }
        control?.Close();
    }

    public abstract Task ExecuteAsync(object? args);

    public async Task<bool> RunAsync(object? args)
    {
        var isExecuted = false;
        var canExecute = false;
        string? exceptionMessage = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            canExecute = CanExecute();
            if (canExecute)
            {
                isExecuted = true; // consider as executed even upon exceptions
                await ExecuteAsync(args);
            }
        }
        catch (Exception exception)
        {
            exceptionMessage = exception.Message;
        }
        finally
        {
            stopwatch.Stop();
            CommandLogger.Log(new CommandExecutionRecord(
                DateTime.Now, Title, Control?.Name, args, canExecute, isExecuted, exceptionMessage, stopwatch.Elapsed));
        }
        return isExecuted;
    }

    public virtual async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = false;
        if (Key?.IsIdentical(keyEvent) ?? false)
        {
            isHandled = await RunAsync(null);
        }
        return isHandled;
    }

    protected static string CreateDisplayTextFromTypeName(string typeName, string suffixToTrim)
    {
        var name = typeName;
        if (name.EndsWith(suffixToTrim, StringComparison.Ordinal))
        {
            name = name[..^suffixToTrim.Length];
        }

        name = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2");
        return name.Trim();
    }

    public static string FormatKey(Key? key)
    {
        if (key is null)
        {
            return string.Empty;
        }

        var modifiers = new List<string>(3);
        if (key.Control)
        {
            modifiers.Add("Ctrl");
        }
        if (key.Shift)
        {
            modifiers.Add("Shift");
        }
        if (key.Alt)
        {
            modifiers.Add("Alt");
        }

        modifiers.Add(FormatConsoleKey(key.ConsoleKey));
        return string.Join('+', modifiers);
    }

    /// <summary>
    /// The key as it is printed on the keyboard. <see cref="ConsoleKey"/>'s own names are the
    /// virtual-key constants — the help screen offered "Ctrl+Oem5" for jumping to the root folder,
    /// which nobody can find on a keyboard. The OEM keys are named by their US-layout character, the
    /// layout Windows names them after.
    /// </summary>
    private static string FormatConsoleKey(ConsoleKey consoleKey)
    {
        if (consoleKey is >= ConsoleKey.D0 and <= ConsoleKey.D9)
        {
            return ((char)('0' + (consoleKey - ConsoleKey.D0))).ToString();
        }

        return consoleKey switch
        {
            ConsoleKey.Oem1 => ";",
            ConsoleKey.Oem2 => "/",
            ConsoleKey.Oem3 => "`",
            ConsoleKey.Oem4 => "[",
            ConsoleKey.Oem5 => "\\",
            ConsoleKey.Oem6 => "]",
            ConsoleKey.Oem7 => "'",
            ConsoleKey.OemPlus => "=",
            ConsoleKey.OemMinus => "-",
            ConsoleKey.OemComma => ",",
            ConsoleKey.OemPeriod => ".",
            ConsoleKey.Add => "Num+",
            ConsoleKey.Subtract => "Num-",
            ConsoleKey.Multiply => "Num*",
            ConsoleKey.Divide => "Num/",
            ConsoleKey.DownArrow => "Down",
            ConsoleKey.UpArrow => "Up",
            ConsoleKey.LeftArrow => "Left",
            ConsoleKey.RightArrow => "Right",
            ConsoleKey.PageUp => "PgUp",
            ConsoleKey.PageDown => "PgDn",
            ConsoleKey.Backspace => "Backspace",
            ConsoleKey.Enter => "Enter",
            ConsoleKey.Escape => "Esc",
            ConsoleKey.Spacebar => "Space",
            _ => consoleKey.ToString()
        };
    }
}
