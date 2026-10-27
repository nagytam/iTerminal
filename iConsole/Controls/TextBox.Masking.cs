using iConsole.Data;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Model.Validations;

namespace iConsole.Controls;

/// <summary>
/// The input mask and the validator pipeline of the text box. With a <see cref="Mask"/> the box types the literals itself,
/// takes only what the slot under the cursor accepts, and shows what is still to type in grey; <see cref="MaskedValue"/> is what
/// the user typed without the literals. Rules added with <see cref="AddValidator(IInputValidator)"/> run in order on every change,
/// and the first one that fails makes the box invalid (<see cref="IsValid"/>, <see cref="ErrorMessage"/>).
/// </summary>
public partial class TextBox
{
    private InputMask? _mask;
    private ValidatorPipeline? _pipeline;

    /// <summary>The pattern the value must follow, or null for free text. Setting it reformats the current text.</summary>
    public InputMask? Mask
    {
        get => _mask;
        set
        {
            _mask = value;
            if (value is not null)
            {
                string formatted = value.Format(value.ExtractRaw(Text));
                Text = formatted;
                ScrollableModel.CursorPosition.X = formatted.Length;
                MaxLength = Math.Max(MaxLength, value.Template.Length);
            }

            StateHasChanged();
        }
    }

    /// <summary>What the user typed, without the mask's literals; the text itself when there is no mask.</summary>
    public string MaskedValue => _mask is null ? Text : _mask.ExtractRaw(Text);

    /// <summary>True when there is no mask, or every required position of it is filled.</summary>
    public bool IsMaskComplete => _mask is null || _mask.IsComplete(MaskedValue);

    /// <summary>The message of the rule that failed, or an empty string.</summary>
    public string ErrorMessage => ValidationModel?.ErrorMessage ?? string.Empty;

    /// <summary>
    /// The rules added so far; null until the first. They see <see cref="MaskedValue"/>, so a rule for numbers works on the digits
    /// of a masked field and not on its separators.
    /// </summary>
    public ValidatorPipeline? Validators => _pipeline;

    /// <summary>Adds a rule to the box's pipeline, creating it (and replacing the default validation) on first use.</summary>
    public TextBox AddValidator(IInputValidator validator)
    {
        if (_pipeline is null)
        {
            _pipeline = new ValidatorPipeline(() => MaskedValue);
            ValidationModel = _pipeline;
        }

        _pipeline.Add(validator);
        StateHasChanged();
        return this;
    }

    public TextBox AddValidator(Func<string, string?> check) => AddValidator(new DelegateValidator(check));

    private void SetMaskedRaw(string raw, int cursorSlots)
    {
        string formatted = _mask!.Format(raw);
        ClearSelection();
        Text = formatted;
        ScrollableModel.CursorPosition.X = _mask.LengthThrough(raw, cursorSlots);
        KeepCursorVisible();
        Parent?.Refresh();
        StateHasChanged();
    }

    /// <summary>Removes the slots the selection covers from <paramref name="raw"/>, returning where the cursor goes.</summary>
    private int RemoveSelectedSlots(ref string raw)
    {
        var (start, end) = SelectedRange;
        int first = _mask!.SlotsBefore(Text, start);
        int last = _mask.SlotsBefore(Text, end);
        raw = raw.Remove(first, Math.Max(0, last - first));
        return first;
    }

    /// <summary>
    /// Typing, Backspace and Delete in a masked box. A character the slot does not take is refused with a beep; the others fill the
    /// slot and push later ones along, as long as each still fits the slot it lands in. Returns whether the key was dealt with.
    /// </summary>
    private bool HandleMaskedKey(KeyEvent keyEvent)
    {
        if (_mask is null || IsReadOnly)
        {
            return false;
        }

        bool typing = ConsoleKeyHelper.IsTypingKey(keyEvent) && ConsoleKeyHelper.IsTypeable(keyEvent.KeyChar)
            && !IsControlKey(keyEvent.ConsoleKey);
        bool backspace = keyEvent.ConsoleKey == ConsoleKey.Backspace && !keyEvent.Control && !keyEvent.Alt;
        bool delete = keyEvent.ConsoleKey == ConsoleKey.Delete && !keyEvent.Control && !keyEvent.Alt;
        if (!typing && !backspace && !delete)
        {
            return false;
        }

        string raw = _mask.ExtractRaw(Text);
        int slot = _mask.SlotsBefore(Text, (int)ScrollableModel.CursorPosition.X);
        if (HasSelection)
        {
            slot = RemoveSelectedSlots(ref raw);
            if (backspace || delete)
            {
                SetMaskedRaw(raw, slot);
                return true;
            }
        }

        string result;
        int cursorSlots;
        if (typing)
        {
            result = raw.Insert(Math.Min(slot, raw.Length), keyEvent.KeyChar.ToString());
            cursorSlots = Math.Min(slot, raw.Length) + 1;
        }
        else if (backspace)
        {
            if (slot == 0)
            {
                return true;
            }

            result = raw.Remove(slot - 1, 1);
            cursorSlots = slot - 1;
        }
        else
        {
            if (slot >= raw.Length)
            {
                return true;
            }

            result = raw.Remove(slot, 1);
            cursorSlots = slot;
        }

        result = new string(result.Select((c, i) => _mask.Normalize(i, c)).ToArray());
        if (!_mask.IsValidRaw(result))
        {
            _beepService.Beep();
            return true;
        }

        SetMaskedRaw(result, cursorSlots);
        return true;
    }

    /// <summary>Keys that move or edit rather than type, whatever character the terminal reports with them.</summary>
    private static bool IsControlKey(ConsoleKey key) => key is ConsoleKey.Backspace or ConsoleKey.Delete or ConsoleKey.LeftArrow
        or ConsoleKey.RightArrow or ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Home or ConsoleKey.End
        or ConsoleKey.PageUp or ConsoleKey.PageDown or ConsoleKey.Insert or ConsoleKey.Tab or ConsoleKey.Enter or ConsoleKey.Escape
        || (key >= ConsoleKey.F1 && key <= ConsoleKey.F24);

    /// <summary>Pasted text in a masked box: each character goes to the next slot that takes it; the rest is dropped.</summary>
    private void InsertMasked(string text)
    {
        string raw = _mask!.ExtractRaw(Text);
        int slot = _mask.SlotsBefore(Text, (int)ScrollableModel.CursorPosition.X);
        if (HasSelection)
        {
            slot = RemoveSelectedSlots(ref raw);
        }

        slot = Math.Min(slot, raw.Length);
        string tail = raw[slot..];
        string head = raw[..slot];
        foreach (char c in ClipboardKeys.NormalizeLineBreaks(text))
        {
            if (head.Length < _mask.SlotCount && _mask.Accepts(head.Length, c))
            {
                head += _mask.Normalize(head.Length, c);
            }
        }

        string result = head + tail;
        if (!_mask.IsValidRaw(result))
        {
            // The old tail no longer fits the slots it was pushed to: keep only what was pasted.
            result = head;
        }

        SetMaskedRaw(result, head.Length);
    }
}
