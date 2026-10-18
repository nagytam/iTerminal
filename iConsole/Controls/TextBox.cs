using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Model.Validations;
using iConsole.Services;

namespace iConsole.Controls;

public class TextBox : Control, IReadOnly
{
    private readonly IBeepService _beepService;
    public string Label { get; set; } = string.Empty;
    public ITextModel TextModel { get; set; } = new TextModel()
    {
        Lines = [string.Empty]
    };
    public string Text 
    {
        get
        {
            return TextModel.Lines[0];
        }
        set
        {
            if (TextModel.Lines[0] != value)
            {
                TextModel.Lines[0] = value;
                TextModel.TriggerPropertyChanged();
                OnPropertyChanged(Text);
            }
        }
    }
    public int TextLength => TextModel.Lines[0].Length;
    public IScrollableModel ScrollableModel { get; private set; } = new ScrollableModel();
    public ISelectionTextModel SelectionTextModel { get; private set; } = new SelectionTextModel();
    public long MaxLength { get; set; } = 256;

    /// <summary>
    /// Hint drawn greyed inside the well while the box is empty, so a field says what it wants
    /// ("Search…", "host:port") without a label. It is never part of <see cref="Text"/>.
    /// </summary>
    public string Placeholder { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; } = false;
    private IValidationModel? _validationModel;
    public IValidationModel? ValidationModel 
    {
        get
        {
            return _validationModel;
        } 
        set
        {
            if (_validationModel != value)
            {
                _validationModel = value;
                OnPropertyChanged(nameof(ValidationModel));
            }
        }
    }
    /// <summary>Whether this box accepts <paramref name="c"/>; any printable character by default.</summary>
    protected virtual bool IsAllowedCharacter(char c) => ConsoleKeyHelper.IsTypeable(c);

    public bool IsValid => (_validationModel is null || _validationModel.IsValid);

    public AutoCompletion? AutoCompletion
    {
        get; set;
    } = null;

    public TextBox(IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService) 
        : base(themeService)
    {
        _beepService = beepService;
        CanFocus = true;

        _validationModel = new ValidationModel<ITextModel>(dateTimeProviderService, TextModel);

        AddCommand(new BackspaceCommand(TextModel, ScrollableModel));
        AddCommand(new DeleteCharacterCommand(TextModel, ScrollableModel));
        AddCommand(new DeleteLineCommand(TextModel, ScrollableModel));

        CommandHelper.AddHorizontalScrollingCommands(this, ScrollableModel, TextModel, SelectionTextModel, true);
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        ScrollableModel.CursorPosition.X = Text.Length;
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (!isHandled && !IsReadOnly &&
            ConsoleKeyHelper.IsTypingKey(keyEvent) &&
            IsAllowedCharacter(keyEvent.KeyChar))
        {
            if (TextLength < MaxLength)
            {
                var newValue = Text;
                if (TextLength < ScrollableModel.CursorPosition.X)
                {
                    newValue = newValue.ExpandRight((int)ScrollableModel.CursorPosition.X);
                }
                newValue = newValue.Insert((int)ScrollableModel.CursorPosition.X, keyEvent.KeyChar.ToString());
                Text = newValue;
                ScrollableModel.CursorPosition.X++;
                if (ScrollableModel.CursorPosition.X > ScrollableModel.ScrollPosition.X + ScrollableModel.ViewSize.Width)
                {
                    ScrollableModel.ScrollPosition.X++;
                }
                isHandled = true;
                Parent?.Refresh();
                if (AutoCompletion is not null && !AutoCompletion.IsVisible)
                {
                    await AutoCompletion.ShowAsync();
                }
            }
            else
            {
                _beepService.Beep();
            }
        }
        return isHandled;
    }

    /// <summary>
    /// What is drawn ahead of the input well: the label and a <c>": "</c> separator, or nothing at
    /// all when there is no label. A label that already ends in a colon ("Path or URL: ") keeps its
    /// own — appending another drew "Path or URL: : [", and an unlabelled box (the one-line inputs
    /// of the Git and package dialogs, or any field hosted in a <see cref="FormPanel"/>,
    /// which draws the labels itself) drew a stray ": " in front of its well.
    /// </summary>
    public virtual string LabelPrefix => FormatLabelPrefix(Label);

    public static string FormatLabelPrefix(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var trimmed = label.TrimEnd();
        return trimmed.EndsWith(':') ? trimmed + " " : trimmed + ": ";
    }

    /// <summary>Column, relative to the control, of the first character inside the well.</summary>
    protected int WellTextOffset => LabelPrefix.Length + 1;

    /// <summary>Number of characters of the value visible inside the well.</summary>
    public int EditableWidth => (int)ViewSize.Width - LabelPrefix.Length - 3;

    /// <summary>The value as it is drawn; <see cref="PasswordBox"/> masks it.</summary>
    protected virtual string DisplayText => Text;

    /// <summary>Whether the greyed autocompletion suggestion is drawn after the value.</summary>
    protected virtual bool ShowsCompletion => true;

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        SyncScrollViewport();
    }

    /// <summary>
    /// Keeps the horizontal scroll viewport the size of the well. Typing scrolls once the cursor
    /// passes <c>ScrollPosition + ViewSize.Width</c>, so a viewport left at zero — every text box
    /// whose dialog did not size it by hand — scrolled on every keystroke and hid what was typed.
    /// </summary>
    private void SyncScrollViewport()
    {
        var width = Math.Max(0, EditableWidth);
        if (ScrollableModel.ViewSize.Width != width || ScrollableModel.ViewSize.Height != 1)
        {
            ScrollableModel.ViewSize = new Size(width, 1);
        }
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        SyncScrollViewport();

        var colorScheme = ThemeService.CurrentColorScheme;
        var visualState = GetVisualState();
        GetColor(colorScheme, visualState, out var foregroundColor, out var backgroundColor);
        var labelBackground = backgroundColor;
        var text = CreateLabel(colorScheme, foregroundColor, backgroundColor);

        foregroundColor = Color.Green;
        backgroundColor = (IsReadOnly)
            ? Color.DarkYellow
            : (IsValid) ? Color.Yellow : colorScheme.Error;

        var displayText = DisplayText;
        var scrollX = Math.Clamp((int)ScrollableModel.ScrollPosition.X, 0, displayText.Length);
        var visibleWidth = Math.Max(0, EditableWidth);
        var startBorder = (scrollX == 0) ? Border.Start : Border.OpenToLeft;
        var endBorder = scrollX + visibleWidth >= displayText.Length ? Border.End : Border.OpenToRight;

        text.Append(startBorder, foregroundColor, backgroundColor);

        // The typed part in the value colour and the suggested remainder greyed after it — both cut
        // to the well, so a long value cannot push the closing bracket off the control.
        var visibleValue = displayText[scrollX..].Cut(visibleWidth);
        var completion = (displayText.Length == 0 && Placeholder.Length > 0
            ? Placeholder
            : GetCompletionSuffix(displayText[scrollX..])).Cut(visibleWidth - visibleValue.Length);
        text.Append(visibleValue, foregroundColor, backgroundColor);
        text.Append(completion, Color.Gray, backgroundColor);
        text.Append(string.Empty.ExpandRight(visibleWidth - visibleValue.Length - completion.Length), foregroundColor, backgroundColor);
        text.Append(endBorder, foregroundColor, backgroundColor);

        text.HighlightSelection(SelectionTextModel, ScrollableModel, WellTextOffset, colorScheme.SelectedFocusedForeground, colorScheme.SelectedFocusedBackground);

        // Fill the rest of the row: nothing clears a layer between frames, so a box laid out wider
        // than its well would otherwise keep whatever was drawn there before.
        text.Expand((int)ViewSize.Width, colorScheme.Foreground, labelBackground);

        buffer.Write(ZOrder, AbsolutePosition, ViewSize, text);

        IsCursorVisible = IsFocused && IsEnabled;
        CursorPosition = new Position(
            (uint)(AbsolutePosition.X + WellTextOffset + ScrollableModel.CursorPosition.X - scrollX),
            AbsolutePosition.Y);
    }

    /// <summary>The <see cref="LabelPrefix"/> in the label colours, with the hot key picked out.</summary>
    protected virtual ConxelString CreateLabel(ColorScheme colorScheme, Color foreground, Color background)
    {
        var label = new ConxelString(LabelPrefix, foreground, background);
        label.HighlightHotKey(HotConsoleKey, HotKeyChar, colorScheme.SelectedFocusedForeground, background);
        return label;
    }

    /// <summary>
    /// The rest of the first autocompletion match after what has been typed, or empty when there
    /// is no completion or it does not extend <paramref name="typed"/>.
    /// </summary>
    private string GetCompletionSuffix(string typed)
    {
        var completion = ShowsCompletion ? AutoCompletion?.CompletionModel?.MatchingValues.FirstOrDefault() : null;
        if (string.IsNullOrEmpty(completion) || !completion.StartsWith(typed))
        {
            return string.Empty;
        }
        return completion[typed.Length..];
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        if (IsReadOnly)
        {
            visualState = VisualState.Disabled;
        }
        switch (visualState)
        {
            case VisualState.Normal:
            default:
                {
                    foregroundColor = colorScheme.Foreground;
                    backgroundColor = colorScheme.Background;
                    break;
                }
            case VisualState.Disabled:
                {
                    foregroundColor = colorScheme.DisabledForeground;
                    backgroundColor = colorScheme.DisabledBackground;
                    break;
                }
            case VisualState.Focused:
                {
                    foregroundColor = colorScheme.SelectedFocusedForeground;
                    backgroundColor = colorScheme.Background;
                    break;
                }
        }
    }
}
