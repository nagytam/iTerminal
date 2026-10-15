using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model;
using System.ComponentModel;

namespace iTerminal.Core.Controls.Terminals;

public class TerminalPanel : Window
{
    private readonly TerminalModel _terminalModel;
    private readonly IContentModelService _contentModelService;
    private int _scrollOffset;

    public TerminalPanel(
        TerminalModel terminalModel,
        IContentModelService contentModelService,
        IThemeService themeService)
        : base(themeService)
    {
        _terminalModel = terminalModel ?? new TerminalModel(new iConsole.Tasks.TaskManager(), null!);
        _contentModelService = contentModelService;
        _terminalModel.PropertyChanged += OnPropertyChanged;
        CanFocus = true;

        var executeCommand = new TerminalExecuteCommand(_terminalModel) { ContentModelService = contentModelService };
        AddCommand(executeCommand);
        AddCommand(new TabForwardCommand());
        AddCommand(new TabBackwardCommand());
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _scrollOffset = 0;
        StateHasChanged();
    }

    public override void OnHandleTabForward()
    {
        var parent = Parent;
        if (parent is null) return;
        var focusable = parent.Controls
            .Where(c => (c is FilePanel || c is AiPromptBar || c is TerminalPanel) && c.IsVisible)
            .ToList();
        var idx = focusable.IndexOf(this);
        focusable[(idx + 1) % focusable.Count]?.SetFocus();
    }

    public override void OnHandleTabBackward()
    {
        var parent = Parent;
        if (parent is null) return;
        var focusable = parent.Controls
            .Where(c => (c is FilePanel || c is AiPromptBar || c is TerminalPanel) && c.IsVisible)
            .ToList();
        var idx = focusable.IndexOf(this);
        focusable[(idx - 1 + focusable.Count) % focusable.Count]?.SetFocus();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            ViewSize = new Size(Parent.ViewSize.Width, Parent.ViewSize.Height);
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
                case ConsoleKey.UpArrow:
                    {
                        ScrollHistory(1);
                        isHandled = true;
                        break;
                    }
                case ConsoleKey.DownArrow:
                    {
                        ScrollHistory(-1);
                        isHandled = true;
                        break;
                    }
                case ConsoleKey.PageUp:
                    {
                        ScrollHistory(Math.Max(1, OutputRows - 1));
                        isHandled = true;
                        break;
                    }
                case ConsoleKey.PageDown:
                    {
                        ScrollHistory(-Math.Max(1, OutputRows - 1));
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
                    break;
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

    /// <summary>
    /// "Terminal", or where the command will run when that is not this machine — <c>web:/etc</c> for a
    /// panel inside a Docker container (<see cref="ITerminalPromptSource"/>).
    /// </summary>
    internal string Prompt =>
        (_contentModelService?.GetLastContentModel() as ITerminalPromptSource)?.TerminalPrompt ?? "Terminal";

    private int OutputRows => Math.Max(0, (int)ViewSize.Height - 1);

    /// <summary>How far back the history is scrolled, in lines from the bottom.</summary>
    internal int ScrollOffset => _scrollOffset;

    /// <summary>
    /// Scrolls the history by <paramref name="lines"/> (positive is back in time). It stops once the
    /// first line is at the top of the panel: the old limit was one line short of the whole history,
    /// so scrolling up ran on into a panel of blank rows with the output all but gone.
    /// </summary>
    private void ScrollHistory(int lines)
    {
        var maxOffset = Math.Max(0, GetHistoryLines().Count - OutputRows);
        _scrollOffset = Math.Clamp(_scrollOffset + lines, 0, maxOffset);
        StateHasChanged();
    }

    /// <summary>Output lines, split on either line ending — a tool's output is not bound to the OS's.</summary>
    private static IEnumerable<string> SplitLines(string text) =>
        text.Split('\n').Select(line => line.TrimEnd('\r'));

    private List<string> GetHistoryLines()
    {
        var lines = new List<string>();
        foreach (var cmd in _terminalModel.History)
        {
            lines.Add($"> {cmd.Command}");
            if (!string.IsNullOrEmpty(cmd.Output))
            {
                lines.AddRange(SplitLines(cmd.Output));
            }
            if (!string.IsNullOrEmpty(cmd.Error))
            {
                foreach (var errLine in SplitLines(cmd.Error))
                {
                    lines.Add($"ERR: {errLine}");
                }
            }
        }
        return lines;
    }

    private static bool IsCommandEditKey(KeyEvent keyEvent) =>
        keyEvent.ConsoleKey is ConsoleKey.Backspace or ConsoleKey.Escape
        || (keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt
            && (keyEvent.ConsoleKey < ConsoleKey.F1 || keyEvent.ConsoleKey > ConsoleKey.F12));

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        var width = (int)ViewSize.Width;
        var height = (int)ViewSize.Height;

        if (height <= 0 || width <= 0)
            return;

        var historyLines = GetHistoryLines();

        // Output area is all rows except the last (command input row)
        var outputRows = height - 1;

        // Determine start index in history with scroll offset
        var totalLines = historyLines.Count;
        var startIdx = Math.Max(0, totalLines - outputRows - _scrollOffset);
        var endIdx = Math.Min(totalLines, startIdx + outputRows);

        for (var row = 0; row < outputRows; row++)
        {
            var lineIdx = startIdx + row;
            var text = lineIdx < endIdx ? historyLines[lineIdx] : string.Empty;
            text = text.ExpandRight(width);

            var isError = text.StartsWith("ERR: ");
            var isCommand = text.StartsWith("> ");
            var fg = isError ? Color.Red : isCommand ? Color.Cyan : Color.Gray;
            var bg = Color.Black;

            var lineStr = new ConxelString(text, fg, bg);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(width, 1), lineStr);
        }

        // Command input line (last row)
        var inputFg = IsFocused ? Color.Yellow : Color.DarkYellow;
        var inputBg = IsFocused ? Color.Black : Color.Gray;
        var prompt = Prompt
            + ((_terminalModel.Command.CommandTask is null) ? ">" : ProgressChar());
        var inputLine = PromptLine.Format(prompt, _terminalModel.Command.Command, width);
        var inputLineStr = new ConxelString(inputLine, inputFg, inputBg);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + height - 1), new Size(width, 1), inputLineStr);
    }
}
