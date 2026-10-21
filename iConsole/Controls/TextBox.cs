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
                ClearSelection();
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

    /// <summary>Where the selection started; the selection runs from here to the cursor. Null when nothing is selected.</summary>
    private long? _selectionAnchor;

    /// <summary>Whether any characters are selected.</summary>
    public bool HasSelection => _selectionAnchor is long anchor && anchor != ScrollableModel.CursorPosition.X;

    /// <summary>The selected text, or an empty string.</summary>
    public string SelectedText
    {
        get
        {
            if (!HasSelection)
            {
                return string.Empty;
            }

            var (start, end) = SelectedRange;
            return Text[start..end];
        }
    }

    private (int Start, int End) SelectedRange
    {
        get
        {
            var anchor = Math.Clamp(_selectionAnchor ?? 0, 0, TextLength);
            var cursor = Math.Clamp(ScrollableModel.CursorPosition.X, 0, TextLength);
            return ((int)Math.Min(anchor, cursor), (int)Math.Max(anchor, cursor));
        }
    }

    private void ClearSelection()
    {
        _selectionAnchor = null;
        SelectionTextModel.IsSelected = false;
        SelectionTextModel.Start = null;
        SelectionTextModel.End = null;
    }

    /// <summary>Publishes the selection to <see cref="SelectionTextModel"/>, whose end is the last selected column.</summary>
    private void UpdateSelectionModel()
    {
        if (!HasSelection)
        {
            ClearSelection();
            return;
        }

        var (start, end) = SelectedRange;
        SelectionTextModel.IsSelected = true;
        SelectionTextModel.Start = new Position(start, 0);
        SelectionTextModel.End = new Position(end - 1, 0);
    }

    /// <summary>Removes the selected characters and puts the cursor where they were.</summary>
    private void DeleteSelection()
    {
        var (start, end) = SelectedRange;
        var remaining = Text.Remove(start, end - start);
        ClearSelection();
        Text = remaining;
        ScrollableModel.CursorPosition.X = start;
        KeepCursorVisible();
    }

    /// <summary>
    /// Shift with Left, Right, Home or End extends the selection from where it began (the cursor, at the first press);
    /// with a selection up, Left and Right collapse it to its start or end, and Backspace and Delete remove it. Typing
    /// replaces it. Any other key that acts clears it.
    /// </summary>
    private bool HandleSelectionKey(KeyEvent keyEvent)
    {
        if (keyEvent.Alt || keyEvent.Control)
        {
            return false;
        }

        var cursor = ScrollableModel.CursorPosition.X;
        if (keyEvent.Shift)
        {
            long? target = keyEvent.ConsoleKey switch
            {
                ConsoleKey.LeftArrow => Math.Max(0, cursor - 1),
                ConsoleKey.RightArrow => Math.Min(TextLength, cursor + 1),
                ConsoleKey.Home => 0,
                ConsoleKey.End => TextLength,
                _ => null,
            };
            if (target is null)
            {
                return false;
            }

            _selectionAnchor ??= cursor;
            ScrollableModel.CursorPosition.X = target.Value;
            UpdateSelectionModel();
            KeepCursorVisible();
            StateHasChanged();
            return true;
        }

        if (!HasSelection)
        {
            return false;
        }

        var (start, end) = SelectedRange;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.LeftArrow:
                ClearSelection();
                ScrollableModel.CursorPosition.X = start;
                break;
            case ConsoleKey.RightArrow:
                ClearSelection();
                ScrollableModel.CursorPosition.X = end;
                break;
            case ConsoleKey.Backspace or ConsoleKey.Delete when !IsReadOnly:
                DeleteSelection();
                break;
            default:
                return false;
        }

        KeepCursorVisible();
        StateHasChanged();
        return true;
    }

    private ITextClipboardService? _clipboard;

    /// <summary>The clipboard Copy, Cut and Paste use: the application's (<see cref="TextClipboard.Current"/>) unless one is given.</summary>
    public ITextClipboardService Clipboard
    {
        get => _clipboard ?? TextClipboard.Current;
        set => _clipboard = value;
    }

    /// <summary>Whether a selection may be copied or cut out of the box. A password box says no.</summary>
    protected virtual bool AllowsCopyOut => true;

    /// <summary>Selects the whole value, with the cursor at its end.</summary>
    public void SelectAll()
    {
        if (TextLength == 0)
        {
            return;
        }

        _selectionAnchor = 0;
        ScrollableModel.CursorPosition.X = TextLength;
        UpdateSelectionModel();
        KeepCursorVisible();
        StateHasChanged();
    }

    /// <summary>
    /// Puts <paramref name="text"/> in the box at the cursor, in place of a selection. A one-line box has no line
    /// breaks: each becomes a blank. Characters the box does not accept are dropped, and what would pass
    /// <see cref="MaxLength"/> is cut off (with a beep, as typing would give).
    /// </summary>
    public void InsertText(string text)
    {
        if (IsReadOnly)
        {
            return;
        }

        var cleaned = new System.Text.StringBuilder();
        foreach (var c in ClipboardKeys.NormalizeLineBreaks(text))
        {
            if (c == '\n')
            {
                cleaned.Append(' ');
            }
            else if (IsAllowedCharacter(c))
            {
                cleaned.Append(c);
            }
        }

        if (cleaned.Length == 0)
        {
            return;
        }

        if (HasSelection)
        {
            DeleteSelection();
        }

        var room = (int)Math.Max(0, MaxLength - TextLength);
        var insert = cleaned.Length > room ? cleaned.ToString(0, room) : cleaned.ToString();
        if (insert.Length < cleaned.Length)
        {
            _beepService.Beep();
        }

        var position = (int)Math.Min(ScrollableModel.CursorPosition.X, TextLength);
        Text = Text.Insert(position, insert);
        ScrollableModel.CursorPosition.X = position + insert.Length;
        KeepCursorVisible();
        Parent?.Refresh();
        StateHasChanged();
    }

    /// <summary>Copy, Cut, Paste and Select all, on the keys <see cref="ClipboardKeys"/> names. A key the box cannot act on is left alone.</summary>
    private async Task<bool> HandleClipboardKeyAsync(KeyEvent keyEvent)
    {
        switch (ClipboardKeys.ActionFor(keyEvent))
        {
            case ClipboardKeyAction.SelectAll:
                if (TextLength == 0)
                {
                    return false;
                }

                SelectAll();
                return true;
            case ClipboardKeyAction.Copy when HasSelection && AllowsCopyOut:
                await ClipboardKeys.TrySetAsync(Clipboard, SelectedText);
                return true;
            case ClipboardKeyAction.Cut when HasSelection && AllowsCopyOut:
                await ClipboardKeys.TrySetAsync(Clipboard, SelectedText);
                if (!IsReadOnly)
                {
                    DeleteSelection();
                }

                return true;
            case ClipboardKeyAction.Paste when !IsReadOnly:
                var text = await ClipboardKeys.TryGetAsync(Clipboard);
                if (string.IsNullOrEmpty(text))
                {
                    return false;
                }

                InsertText(text);
                return true;
            default:
                return false;
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        KeepCursorVisible();
        if (IsFocused && IsEnabled && HandleSelectionKey(keyEvent))
        {
            return true;
        }

        if (IsFocused && IsEnabled && await HandleClipboardKeyAsync(keyEvent))
        {
            return true;
        }


        // The scrolling command for Right stops at the edge of the well, which the cursor may reach but not pass, so
        // it is handled here: one step right, and the view follows.
        if (IsFocused && IsEnabled && keyEvent.ConsoleKey == ConsoleKey.RightArrow
            && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            if (ScrollableModel.CursorPosition.X < TextLength)
            {
                ScrollableModel.CursorPosition.X++;
            }

            KeepCursorVisible();
            StateHasChanged();
            return true;
        }

        var isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (!isHandled && !IsReadOnly &&
            ConsoleKeyHelper.IsTypingKey(keyEvent) &&
            IsAllowedCharacter(keyEvent.KeyChar))
        {
            if (HasSelection && !IsReadOnly)
            {
                DeleteSelection();
            }

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
        if (isHandled)
        {
            // A key that acted other than by selecting (Shift is the selecting modifier) ends the selection.
            if (!keyEvent.Shift)
            {
                ClearSelection();
            }

            KeepCursorVisible();
        }

        return isHandled;
    }

    /// <summary>
    /// Brings the cursor back onto the text and the view round it: the cursor is kept between the first and the last
    /// character (a value shortened from outside left it past the end), the scrollable size follows the length so the
    /// scrolling commands — which stop at it — know how far they may go, and the view scrolls to keep the cursor in
    /// the well. Before this the cursor could not move right of the well's edge in a long value, End did not scroll to
    /// the end of it, and a value shortened while scrolled drew an empty well.
    /// </summary>
    private void KeepCursorVisible() => ClampToText(followCursor: true);

    /// <summary>
    /// The part of <see cref="KeepCursorVisible"/> that is safe on every frame: the cursor on the text, the scrollable
    /// size following the length and the scroll no further than the end of the value. Only after a key does the view
    /// also follow the cursor, so a scroll position set from outside is not undone by drawing.
    /// </summary>
    private void ClampToText(bool followCursor)
    {
        SyncScrollViewport();
        ScrollableModel.ScrollableSize = new Size(TextLength, 1);
        ScrollableModel.CursorPosition.Y = 0;
        ScrollableModel.CursorPosition.X = Math.Clamp(ScrollableModel.CursorPosition.X, 0, TextLength);
        var well = ScrollableModel.ViewSize.Width;
        if (well > 0)
        {
            // The cursor may sit on the cell after the last visible character (the closing bracket), as it does at the
            // end of a value that just fits. The value is never scrolled further than leaves its end at the right edge:
            // a value shortened while scrolled drew an empty well.
            var cursor = ScrollableModel.CursorPosition.X;
            var scroll = ScrollableModel.ScrollPosition.X;
            if (followCursor && cursor < scroll)
            {
                scroll = cursor;
            }
            else if (followCursor && cursor > scroll + well)
            {
                scroll = cursor - well;
            }

            ScrollableModel.ScrollPosition.X = Math.Clamp(scroll, 0, Math.Max(0, TextLength - well));
        }
    }

    /// <summary>
    /// A click in the well takes the focus and puts the cursor under the pointer (past the end of
    /// the text it lands on the end), as in any text field. Before this the mouse could not place
    /// the cursor in a long value.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (isHandled || !mouseEvent.LeftClicked || !IsEnabled || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        if (!IsFocused)
        {
            SetFocus();
        }

        ClearSelection();
        long column = (long)mouseEvent.Position.X - (long)AbsolutePosition.X - WellTextOffset;
        long target = Math.Clamp(column + (long)ScrollableModel.ScrollPosition.X, 0, TextLength);
        ScrollableModel.CursorPosition.X = target;
        StateHasChanged();
        return true;
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
        ClampToText(followCursor: false);
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
        ClampToText(followCursor: false);

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
