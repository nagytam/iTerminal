using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Prompts;

/// <summary>
/// A dialog that asks one question and resolves <see cref="Result"/> once: with the answer, or with the default when it is
/// closed without one (Esc). The caller awaits the result; it never sees the dialog's controls.
/// </summary>
public abstract class PromptDialog<TResult> : Dialog
{
    private readonly TaskCompletionSource<TResult?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected PromptDialog(string title, IThemeService themeService) : base(title, themeService)
    {
    }

    /// <summary>The answer, or the default (null for a reference or nullable type) when the prompt was cancelled.</summary>
    public Task<TResult?> Result => _result.Task;

    /// <summary>What the dialog says under the question: an error, or nothing.</summary>
    public string Message { get; protected set; } = string.Empty;

    protected void Answer(TResult? value)
    {
        _result.TrySetResult(value);
        Close();
    }

    public override void Close()
    {
        _result.TrySetResult(default);
        base.Close();
    }

    /// <summary>Writes one row of the dialog's content, padded to the content width. Row 0 is the first row under the top border.</summary>
    protected void WriteRow(IBuffer buffer, int row, string text, Color foreground, Color background, bool isBold = false)
    {
        var width = ContentWidth;
        if (width <= 0)
        {
            return;
        }

        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 2, AbsolutePosition.Y + 1 + row),
            new Size(width, 1),
            new ConxelString(TextWidth.Fit(text, width), foreground, background, isBold));
    }

    protected int ContentWidth => (int)ViewSize.Width - 4;

    protected Color Foreground => ThemeService.CurrentColorScheme.Foreground;

    protected Color Background => ThemeService.CurrentColorScheme.Background;

    protected Color Hint => ThemeService.CurrentColorScheme.DisabledForeground;

    protected Color Error => ThemeService.CurrentColorScheme.Error;

    /// <summary>A dialog is as wide as its question needs, within the range a terminal shows comfortably.</summary>
    protected static long WidthFor(string question, int minimum = 40, int maximum = 80) =>
        Math.Clamp(TextWidth.Of(question) + 6, minimum, maximum);
}

/// <summary>
/// Text input. Enter checks the text with the prompt's rules and answers when it passes; otherwise the reason is shown and the
/// dialog stays open. Everything else is typing, handled by the text box.
/// </summary>
public sealed class TextPromptDialog<T> : PromptDialog<T>
{
    private readonly TextPrompt<T> _prompt;
    private readonly TextBox _box;

    public TextPromptDialog(TextPrompt<T> prompt, IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService)
        : base("Prompt", themeService)
    {
        _prompt = prompt;
        _box = prompt.IsSecret
            ? new PasswordBox(beepService, dateTimeProviderService, themeService)
            : new TextBox(beepService, dateTimeProviderService, themeService);
        _box.Text = prompt.DefaultValue ?? string.Empty;
        AddControl(_box);
    }

    /// <summary>The text in the box, as typed.</summary>
    public string Text => _box.Text;

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        CenterInParent(new Size(WidthFor(_prompt.Question), 6));
        _box.Position = new Position(2, 2);
        _box.ViewSize = new Size(Math.Max(1, ContentWidth), 1);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("Hint.PromptAccept", "Accept the value"));
        yield return ("Esc", UiText.Get("Hint.PromptCancel", "Cancel"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            if (_prompt.TryParse(_box.Text, out var value, out var error))
            {
                Answer(value);
            }
            else
            {
                Message = error ?? "Enter a value.";
                StateHasChanged();
            }
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        WriteRow(buffer, 0, _prompt.Question, Foreground, Background, isBold: true);
        WriteRow(buffer, 2, Message, Error, Background);
        WriteRow(buffer, 3, "Enter: accept    Esc: cancel", Hint, Background);
    }
}

/// <summary>The shared layout and keys of the selection prompts: a filter line, a scrolling list and a hint.</summary>
public abstract class ChoicePromptDialog<T, TResult> : PromptDialog<TResult>
{
    private readonly ChoicePrompt<T> _prompt;

    protected ChoicePromptDialog(ChoicePrompt<T> prompt, string title, IThemeService themeService) : base(title, themeService)
    {
        _prompt = prompt;
        List = new ChoiceList<T>(prompt.Choices, prompt.Label);
    }

    protected ChoiceList<T> List { get; }

    /// <summary>The rows the list shows. A prompt with no choices still shows one, so the dialog never collapses.</summary>
    protected int PageRows => Math.Max(1, _prompt.PageSize);

    /// <summary>The key line at the bottom of the dialog.</summary>
    protected abstract string HintText { get; }

    /// <summary>Enter. Each prompt decides what choosing means.</summary>
    protected abstract void OnEnter();

    /// <summary>Whether a typed character goes into the filter. Space is not typed into the filter when it marks choices.</summary>
    protected virtual bool TypesIntoFilter(char c) => true;

    /// <summary>A key that is not navigation, filtering or Enter. Multi-selection uses Space here.</summary>
    protected virtual bool HandleOtherKey(KeyEvent keyEvent) => false;

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        CenterInParent(new Size(WidthFor(_prompt.Question), PageRows + 6));
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Up/Down", UiText.Get("Hint.PromptMove", "Move the selection"));
        yield return ("Enter", UiText.Get("Hint.PromptAccept", "Accept"));
        yield return ("Esc", UiText.Get("Hint.PromptCancel", "Cancel"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.Control || keyEvent.Alt)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Enter when !keyEvent.Shift:
                OnEnter();
                return true;
            case ConsoleKey.UpArrow:
                List.Move(-1);
                return true;
            case ConsoleKey.DownArrow:
                List.Move(1);
                return true;
            case ConsoleKey.PageUp:
                List.Move(-PageRows);
                return true;
            case ConsoleKey.PageDown:
                List.Move(PageRows);
                return true;
            case ConsoleKey.Home:
                List.MoveTo(0);
                return true;
            case ConsoleKey.End:
                List.MoveTo(int.MaxValue);
                return true;
            case ConsoleKey.Backspace:
                List.Backspace();
                return true;
        }

        if (HandleOtherKey(keyEvent))
        {
            return true;
        }

        if (IsPrintable(keyEvent.KeyChar) && TypesIntoFilter(keyEvent.KeyChar))
        {
            List.Type(keyEvent.KeyChar);
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        WriteRow(buffer, 0, _prompt.Question, Foreground, Background, isBold: true);
        WriteRow(buffer, 1, "Filter: " + List.Filter, Foreground, Background);

        var top = List.ScrollTop(PageRows);
        for (var row = 0; row < PageRows; row++)
        {
            var index = top + row;
            if (index >= List.VisibleCount)
            {
                WriteRow(buffer, 2 + row, index == 0 ? "No choices match the filter." : string.Empty, Hint, Background);
                continue;
            }

            var highlighted = index == List.Cursor;
            var marker = highlighted ? "› " : "  ";
            var text = marker + (MarksChoices ? (List.IsMarked(index) ? "[x] " : "[ ] ") : string.Empty) + List.LabelAt(index);
            var scheme = ThemeService.CurrentColorScheme;
            var fg = highlighted ? scheme.SelectedFocusedForeground : scheme.Foreground;
            var bg = highlighted ? scheme.SelectedFocusedBackground : scheme.Background;
            WriteRow(buffer, 2 + row, text, fg, bg);
        }

        WriteRow(buffer, 2 + PageRows, Message, Error, Background);
        WriteRow(buffer, 3 + PageRows, HintText, Hint, Background);
    }

    /// <summary>Whether the rows show a check box in front of each choice.</summary>
    protected virtual bool MarksChoices => false;

    private static bool IsPrintable(char c) => c >= ' ' && c != '\u007F';
}

/// <summary>Pick one choice. Enter chooses the highlighted one.</summary>
public sealed class SelectionPromptDialog<T> : ChoicePromptDialog<T, T>
{
    public SelectionPromptDialog(SelectionPrompt<T> prompt, IThemeService themeService)
        : base(prompt, "Select", themeService)
    {
    }

    protected override string HintText => "Type to filter    Enter: choose    Esc: cancel";

    protected override void OnEnter()
    {
        if (List.VisibleCount == 0)
        {
            Message = "Nothing matches the filter.";
            StateHasChanged();
            return;
        }

        Answer(List.Current);
    }
}

/// <summary>Pick any number of choices. Space marks the highlighted one; Enter confirms once enough are marked.</summary>
public sealed class MultiSelectionPromptDialog<T> : ChoicePromptDialog<T, IReadOnlyList<T>>
{
    private readonly int _minimum;

    public MultiSelectionPromptDialog(MultiSelectionPrompt<T> prompt, IThemeService themeService)
        : base(prompt, "Select", themeService)
    {
        _minimum = prompt.MinimumSelections;
    }

    protected override bool MarksChoices => true;

    protected override string HintText => "Space: mark    Enter: confirm    Esc: cancel";

    protected override bool TypesIntoFilter(char c) => c != ' ';

    protected override bool HandleOtherKey(KeyEvent keyEvent)
    {
        if (keyEvent.KeyChar == ' ' && !keyEvent.Shift)
        {
            List.ToggleCurrent();
            Message = string.Empty;
            return true;
        }
        return false;
    }

    protected override void OnEnter()
    {
        var marked = List.MarkedItems;
        if (marked.Count < _minimum)
        {
            Message = $"Mark at least {_minimum}.";
            StateHasChanged();
            return;
        }

        Answer(marked);
    }
}

/// <summary>Y or N. Enter takes the default when there is one.</summary>
public sealed class ConfirmationPromptDialog : PromptDialog<bool?>
{
    private readonly ConfirmationPrompt _prompt;

    public ConfirmationPromptDialog(ConfirmationPrompt prompt, IThemeService themeService) : base("Confirm", themeService)
    {
        _prompt = prompt;
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        CenterInParent(new Size(WidthFor(_prompt.Question), 6));
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Y/N", UiText.Get("Hint.PromptYesNo", "Answer yes or no"));
        yield return ("Enter", UiText.Get("Hint.PromptDefault", "Take the default"));
        yield return ("Esc", UiText.Get("Hint.PromptCancel", "Cancel"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            switch (char.ToLowerInvariant(keyEvent.KeyChar))
            {
                case 'y':
                    Answer(true);
                    return true;
                case 'n':
                    Answer(false);
                    return true;
            }

            if (keyEvent.ConsoleKey == ConsoleKey.Enter && _prompt.DefaultValue is { } fallback)
            {
                Answer(fallback);
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        WriteRow(buffer, 0, _prompt.Question, Foreground, Background, isBold: true);
        WriteRow(buffer, 2, "[Y] Yes    [N] No", Foreground, Background);
        var fallback = _prompt.DefaultValue switch
        {
            true => "Enter: yes    Esc: cancel",
            false => "Enter: no    Esc: cancel",
            _ => "Esc: cancel",
        };
        WriteRow(buffer, 3, fallback, Hint, Background);
    }
}
