using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Model;
using System.ComponentModel;

namespace iTerminal.Core.Controls.Terminals;

public class TerminalWindow : Window
{
    private IContentModelService _contentModelService;
    private TerminalModel _terminalModel { get; set; }

    //public override bool IsEnabled 
    //{ 
    //    get  
    //    {
    //        try
    //        {
    //            var contentModel = _contentModelService.GetLastContentModel();
    //            if (contentModel != null)
    //            {
    //                switch (contentModel.Url.UrlScheme)
    //                {
    //                    case iConsole.Data.Container.MEMORY_DRIVE:
    //                        {
    //                            return false;
    //                        }
    //                }
    //            }
    //        }
    //        catch
    //        {

    //        }
    //        return base.IsEnabled; 
    //    }
    //    set
    //    {
    //        if (base.IsEnabled != value)
    //        {
    //            base.IsEnabled = value;
    //        }
    //    }
    //}

    public TerminalWindow(
        TerminalModel terminalModel,
        IContentModelService contentModelService,
        IThemeService themeService) 
        : base(themeService)
    {
        _contentModelService = contentModelService;
        _terminalModel = terminalModel ?? new TerminalModel(new iConsole.Tasks.TaskManager(), null!);
        _terminalModel.PropertyChanged += OnPropertyChanged;
        CanFocus = true;

        var command = new TerminalExecuteCommand(_terminalModel) { ContentModelService = contentModelService };
        AddCommand(command);
        AddCommand(new TabForwardCommand());
        AddCommand(new TabBackwardCommand());
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            ViewSize = new Size(Parent.ViewSize.Width, 1);
        }
        base.OnInitializeLayout();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = await base.HandleKeyEventAsync(keyEvent);

        if (!isHandled && _terminalModel.IsRunning && IsCommandEditKey(keyEvent))
        {
            // The command being typed *is* the running command until it finishes and is moved to
            // the history, so an edit now would rewrite the history entry of what is running.
            isHandled = true;
        }

        if (!isHandled)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Backspace:
                    {
                        if (_terminalModel.Command.Command.Length > 0)
                        {
                            _terminalModel.Command.Command = 
                                _terminalModel.Command.Command[..^1];
                        }
                        isHandled = true;
                        break;
                    }
                case ConsoleKey.Escape when _terminalModel.Command.Command.Length > 0:
                    {
                        // Clearing a half-typed command took one Backspace per character.
                        _terminalModel.Command.Command = string.Empty;
                        isHandled = true;
                        break;
                    }
                case ConsoleKey.F1:
                case ConsoleKey.F2:
                case ConsoleKey.F3:
                case ConsoleKey.F4:
                case ConsoleKey.F5:
                case ConsoleKey.F6:
                case ConsoleKey.F7:
                case ConsoleKey.F8:
                case ConsoleKey.F9:
                case ConsoleKey.F10:
                case ConsoleKey.F11:
                case ConsoleKey.F12:
                    {
                        break;
                    }
                default:
                    {
                        // Printable characters only: Esc, Tab and friends carry a KeyChar too, and
                        // used to be typed into the command as invisible control characters.
                        if (keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt)
                        {
                            _terminalModel.Command.Command += keyEvent.KeyChar;
                            isHandled = true;
                        }
                        break;
                    }
            }
        }

        return isHandled;
    }

    private static bool IsCommandEditKey(KeyEvent keyEvent) =>
        keyEvent.ConsoleKey is ConsoleKey.Backspace or ConsoleKey.Escape
        || (keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt
            && (keyEvent.ConsoleKey < ConsoleKey.F1 || keyEvent.ConsoleKey > ConsoleKey.F12));

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var foreground = (IsFocused) ? Color.Yellow : Color.DarkYellow;
        var background = (IsFocused) ? Color.Black : Color.Gray;
        var prompt = "Terminal" 
            + ((_terminalModel.Command.CommandTask is null) ? ">" : ProgressChar());
        var line = PromptLine.Format(prompt, _terminalModel.Command.Command, (int)ViewSize.Width);
        var lineStr = new ConxelString(line, foreground, background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, lineStr);
    }
}
