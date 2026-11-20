using iConsole.Localization;
using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Dialogs;

public class HelpDialog : Dialog
{
    private TextViewerControl _textViewerControl { get; set; }
    public IControl? ContextControl { get; set; }
    public IControl? ContextFocus { get; set; }

    public HelpDialog(
        IThemeService themeService)
        : base(nameof(HelpDialog), themeService)
    {
        _textViewerControl = new TextViewerControl(false, themeService);
        Title = iConsole.Localization.UiText.Get("Common.Help", "Help");

        AddControl(_textViewerControl);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        Title = iConsole.Localization.UiText.Get("Common.Help", "Help");

        var rootControl = ContextControl ?? GetRootControl();
        var focusedControl = ContextFocus ?? rootControl.GetFocusedControl();
        // The dialog fills its parent, less the frame and the scroll bar.
        var width = Parent is not null ? (int)Parent.ViewSize.Width - 4 : int.MaxValue;
        var help = GetAvailableCommandsHelp(rootControl, focusedControl, width);
        _textViewerControl.TextModel.Lines = [.. help.Split(Environment.NewLine)];
    }

    /// <summary>
    /// <paramref name="line"/> broken at word boundaries to fit <paramref name="width"/>; the lines after the first hang two columns
    /// in from where the text began, so a wrapped item still reads as one. A line that fits comes back as it is.
    /// </summary>
    internal static IEnumerable<string> WrapToWidth(string line, int width)
    {
        if (width <= 0 || line.Length <= width)
        {
            yield return line;
            yield break;
        }

        var first = line.WrapWords(width)[0];
        yield return first;

        var hang = new string(' ', Math.Min(line.Length - line.TrimStart().Length + 2, Math.Max(0, width - 10)));
        foreach (var part in line[first.Length..].TrimStart().WrapWords(width - hang.Length))
        {
            yield return hang + part;
        }
    }

    private const int MinimumKeyColumn = 18;
    private const int MaximumKeyColumn = 24;

    /// <summary>
    /// Orders keys the way a reader scans for them: plain keys first, then Shift, Ctrl, Alt and
    /// their combinations, and function keys by number — so F2 comes before F11, which a plain string
    /// sort put the other way round (Alt+F11, Alt+F12, Alt+F9).
    /// </summary>
    internal static (int Modifiers, int KeyGroup, int Number, string Text) KeySortOrder(string keyDisplayText)
    {
        var chord = keyDisplayText.Split(" / ")[0];
        var parts = chord.Split('+');
        var key = parts[^1];
        var modifiers = 0;
        foreach (var modifier in parts[..^1])
        {
            modifiers += modifier switch
            {
                "Shift" => 1,
                "Ctrl" => 2,
                "Alt" => 4,
                _ => 8,
            };
        }
        if (key.Length > 1 && key[0] == 'F' && int.TryParse(key[1..], out var number))
        {
            return (modifiers, 1, number, key);
        }
        return (modifiers, key.Length == 1 ? 0 : 2, 0, key);
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
        }
        _textViewerControl.Position = new Position(1, 1);
        var viewerWidth = Math.Max(0, ViewSize.Width - 2);
        var viewerHeight = Math.Max(0, ViewSize.Height - 2);
        _textViewerControl.ViewSize = new Size(viewerWidth, viewerHeight);
        base.OnInitializeLayout();
    }

    /// <summary>
    /// A control's name as a reader sees it: a translated name when the resources have one, otherwise the
    /// name with its words split in sentence case ("LeftPanel" reads "Left panel"). The internal name used to be shown as it is.
    /// </summary>
    internal static string ReadableName(string name)
    {
        var words = string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) ? " " + char.ToLowerInvariant(c) : c.ToString()));
        return UiText.Get("Control.Name." + name, words);
    }

    /// <summary>One "Getting started" line: the key in a column of its own, so every description starts in the same place.</summary>
    internal static string StartedLine(string key, string description)
    {
        return "  " + key.PadRight(MinimumKeyColumn) + " " + description;
    }

    private static string GetAvailableCommandsHelp(IControl rootControl, IControl? focusedControl, int width)
    {
        var commands = GetAvailableCommands(rootControl)
            .OrderBy(c => KeySortOrder(c.KeyDisplayText))
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<string>
        {
            // The help is a long list with no footer, so it says how to leave it and how to read it.
            UiText.Get("Help.CloseHint", "Esc: close this help   ↑↓ PgUp PgDn: scroll it"),
            string.Empty,
            UiText.Get("Help.Started.Heading", "Getting started:"),
            StartedLine(UiText.Get("Help.Started.Tab.Key", "Tab / Shift+Tab"), UiText.Get("Help.Started.Tab", "Move focus between panels and controls")),
            StartedLine(UiText.Get("Help.Started.Enter.Key", "Enter"), UiText.Get("Help.Started.Enter", "Run the focused item's main command")),
            StartedLine(UiText.Get("Help.Started.Esc.Key", "Esc"), UiText.Get("Help.Started.Esc", "Close the dialog or menu in front of you")),
            StartedLine(UiText.Get("Help.Started.F1.Key", "F1"), UiText.Get("Help.Started.F1", "Open this help from any screen")),
            StartedLine(UiText.Get("Help.Started.Palette.Key", "Shift+F1"), UiText.Get("Help.Started.Palette", "Find any command by name in the command palette")),
            "  " + UiText.Get("Help.Started.Lists", "The first list below works here and now."),
            "  " + UiText.Get("Help.Started.AllLists", "The second lists every command, even those that cannot run in this context."),
            string.Empty,
            focusedControl is not null
                ? string.Format(UiText.Get("Help.FocusedControl", "Focused control: {0}"), ReadableName(focusedControl.Name))
                : UiText.Get("Help.FocusedControlNone", "Focused control: n/a"),
            string.Empty,
            UiText.Get("Help.AvailableCommands", "Available commands:"),
        };

        // The lines above are fixed text, written for a wide screen; on a narrow one the frame would cut them mid-word.
        lines = [.. lines.SelectMany(line => WrapToWidth(line, width))];

        if (commands.Count == 0)
        {
            lines.Add("  " + UiText.Get("Help.NoCommands", "No keyboard commands are currently available."));
        }
        else
        {
            // The key column fits the keys, within reason; a key longer than that gets a line of its
            // own rather than pushing its title out of the column ("Ctrl+Shift+M / Ctrl+F7 Music…").
            var keyColumn = Math.Clamp(commands.Max(c => c.KeyDisplayText.Length), MinimumKeyColumn, MaximumKeyColumn);
            // On a narrow terminal the descriptions keep 24 columns to wrap in; a longer key takes a line of its own.
            keyColumn = Math.Min(keyColumn, Math.Max(8, width - 3 - 24));
            var indent = new string(' ', keyColumn + 3);
            foreach (var command in commands)
            {
                var key = command.KeyDisplayText;
                if (key.Length > keyColumn)
                {
                    lines.Add($"  {key}");
                    lines.Add(indent + UiText.Title(command.Title));
                }
                else
                {
                    lines.Add($"  {key.PadRight(keyColumn)} {UiText.Title(command.Title)}");
                }
                // A description that only repeats the title ("Toggle Terminal" / "Toggle Terminal")
                // is a line of noise per command; a long one is wrapped, not cut at the frame.
                var description = UiText.Describe(command.Description?.Trim() ?? string.Empty);
                if (description.Length > 0 && !string.Equals(description.TrimEnd('.'), command.Title, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var part in description.WrapWords(Math.Max(20, width - indent.Length)))
                    {
                        lines.Add(indent + part);
                    }
                }
            }
        }

        // The list above is what can run here and now. This one is every command the application has,
        // so a key or a dialog not reachable from the focused control is still found: hidden menus and
        // the hot key bar's layers included, and commands that cannot run in this context too.
        lines.Add(string.Empty);
        lines.Add(UiText.Get("Help.AllCommands", "All commands in the application:"));
        var everything = GetEveryCommand(rootControl)
            .OrderBy(c => string.IsNullOrWhiteSpace(c.KeyDisplayText) ? 1 : 0)
            .ThenBy(c => KeySortOrder(c.KeyDisplayText ?? string.Empty))
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var everyKeyColumn = Math.Clamp(everything.Where(c => !string.IsNullOrWhiteSpace(c.KeyDisplayText)).Select(c => c.KeyDisplayText.Length).DefaultIfEmpty(0).Max(), MinimumKeyColumn, MaximumKeyColumn);
        foreach (var command in everything)
        {
            var key = command.KeyDisplayText ?? string.Empty;
            var title = UiText.Title(command.Title.Trim());
            if (key.Length > everyKeyColumn)
            {
                lines.Add($"  {key}");
                lines.Add(new string(' ', everyKeyColumn + 3) + title);
            }
            else
            {
                lines.Add($"  {key.PadRight(everyKeyColumn)} {title}");
            }
        }

        // Several controls of one kind (every text box, every spinner) describe the same keys; list each once.
        var keyHints = GetKeyHints(rootControl).Distinct().ToList();
        if (keyHints.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(UiText.Get("Help.MenuNavigation", "Menu navigation:"));
            foreach (var hint in keyHints)
            {
                lines.Add($"  {hint.Key.PadRight(18)} {hint.Description}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IEnumerable<ICommand> GetAvailableCommands(IControl control)
    {
        if (!control.IsVisible || !control.IsEnabled)
        {
            yield break;
        }

        var seenCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in GetAvailableCommandsInternal(control))
        {
            var identifier = $"{command.KeyDisplayText}|{command.Title}|{command.Description}";
            if (seenCommands.Add(identifier))
            {
                yield return command;
            }
        }
    }

    /// <summary>
    /// Every command with a title under <paramref name="control"/>, hidden and disabled controls included and
    /// no <c>CanExecute</c> check — the complete list, not the one that applies at this moment. Each title and key
    /// pair once, however many controls offer it.
    /// </summary>
    private static IEnumerable<ICommand> GetEveryCommand(IControl control)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in GetEveryCommandInternal(control))
        {
            if (string.IsNullOrWhiteSpace(command.Title))
            {
                continue;
            }

            if (seen.Add($"{command.KeyDisplayText}|{command.Title.Trim()}"))
            {
                yield return command;
            }
        }
    }

    private static IEnumerable<ICommand> GetEveryCommandInternal(IControl control)
    {
        if (control is iConsole.Controls.Control commandControl)
        {
            foreach (var command in commandControl.Commands.AsSnapshot())
            {
                yield return command;
            }
        }

        foreach (var childControl in control.Controls.AsArray())
        {
            foreach (var command in GetEveryCommandInternal(childControl))
            {
                yield return command;
            }
        }
    }

    private static IEnumerable<ICommand> GetAvailableCommandsInternal(IControl control)
    {
        if (!control.IsVisible || !control.IsEnabled)
        {
            yield break;
        }

        if (control is iConsole.Controls.Control commandControl)
        {
            // A control offers a key to its commands in order and the first one that can run takes
            // it, so of several applicable commands on one key only the first ever runs — and only
            // that one is listed. The panel used to list Enter twice, as "Open folder" and "Open or run".
            var keysTaken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var command in commandControl.Commands.AsSnapshot())
            {
                if (string.IsNullOrWhiteSpace(command.KeyDisplayText))
                {
                    continue;
                }

                bool canExecute;
                try
                {
                    canExecute = command.CanExecute();
                }
                catch
                {
                    canExecute = false;
                }

                if (canExecute && keysTaken.Add(command.KeyDisplayText))
                {
                    yield return command;
                }
            }
        }

        foreach (var childControl in control.Controls.AsArray())
        {
            foreach (var command in GetAvailableCommandsInternal(childControl))
            {
                yield return command;
            }
        }
    }

    // Surfaces key bindings controls handle directly in HandleKeyEventAsync (e.g. the menu bar's
    // arrow-key navigation) rather than through a Command, so they still show up in Help.
    private static IEnumerable<(string Key, string Description)> GetKeyHints(IControl control)
    {
        if (!control.IsVisible || !control.IsEnabled)
        {
            yield break;
        }

        if (control is iConsole.Controls.Control hintControl)
        {
            foreach (var hint in hintControl.GetKeyHints())
            {
                yield return hint;
            }
        }

        foreach (var childControl in control.Controls.AsArray())
        {
            foreach (var hint in GetKeyHints(childControl))
            {
                yield return hint;
            }
        }
    }

}
