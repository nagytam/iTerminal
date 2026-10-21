using iConsole.Commands;
using iConsole.Helper;
using iConsole.Data;
using iConsole.Controls.Viewers;
using iConsole.Model;
using iConsole.Services;
using System.ComponentModel;

namespace iConsole.Controls.Editors;

public class TextEditorControl 
    : TextViewerControl, IDirtyModel, IDirtyReadOnlyModel, IReadOnly
{
    private bool _isDirty = false;
    public bool IsDirty
    {
        get
        {
            return _isDirty;
        }
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    private bool _isReadOnly = false;
    public bool IsReadOnly
    {
        get
        {
            return _isReadOnly;
        }
        set
        {
            if (value != _isReadOnly)
            {
                _isReadOnly = value;
                OnPropertyChanged(nameof(IsReadOnly));
            }
        }
    }

    public TextEditorControl(
        IThemeService themeService)
        : base(true, themeService)
    {
        IsCursorVisible = true;

        AddCommand(new TypeCommand());
        AddCommand(new BackspaceCommand(TextModel, ScrollableModel));
        AddCommand(new DeleteCharacterCommand(TextModel, ScrollableModel));
        AddCommand(new DeleteLineCommand(TextModel, ScrollableModel));
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        StateHasChanged();
    }

    private ITextClipboardService? _clipboard;

    /// <summary>The clipboard Copy, Cut and Paste use: the application's (<see cref="TextClipboard.Current"/>) unless one is given.</summary>
    public ITextClipboardService Clipboard
    {
        get => _clipboard ?? TextClipboard.Current;
        set => _clipboard = value;
    }

    /// <summary>
    /// Puts <paramref name="text"/> in at the cursor, in place of a selection: its lines become lines of the text, the
    /// first joined to what is before the cursor and the last to what is after it. Line breaks are normalised and
    /// control characters other than a tab are dropped.
    /// </summary>
    public void InsertText(string text)
    {
        if (IsReadOnly)
        {
            return;
        }

        var cleaned = new string([.. ClipboardKeys.NormalizeLineBreaks(text).Where(c => c is '\n' or '\t' || !char.IsControl(c))]);
        if (cleaned.Length == 0)
        {
            return;
        }

        if (HasSelection)
        {
            RemoveSelectedText();
        }

        var lines = TextModel.Lines;
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        var row = (int)Math.Clamp(ScrollableModel.CursorPosition.Y, 0, lines.Count - 1);
        var line = lines[row];
        var column = (int)Math.Min(ScrollableModel.CursorPosition.X, line.Length);
        var before = line[..column];
        var after = line[column..];
        var parts = cleaned.Split('\n');
        if (parts.Length == 1)
        {
            lines[row] = before + parts[0] + after;
            ScrollableModel.CursorPosition.X = column + parts[0].Length;
        }
        else
        {
            lines[row] = before + parts[0];
            for (var i = 1; i < parts.Length - 1; i++)
            {
                lines.Insert(row + i, parts[i]);
            }

            lines.Insert(row + parts.Length - 1, parts[^1] + after);
            ScrollableModel.CursorPosition.Y = row + parts.Length - 1;
            ScrollableModel.CursorPosition.X = parts[^1].Length;
        }

        IsDirty = true;
        TextModel.TriggerPropertyChanged();
        Refresh();
        KeepCursorInText();
    }

    /// <summary>Copy, Cut, Paste and Select all, on the keys <see cref="ClipboardKeys"/> names. A key the editor cannot act on is left alone.</summary>
    private async Task<bool> HandleClipboardKeyAsync(KeyEvent keyEvent)
    {
        switch (ClipboardKeys.ActionFor(keyEvent))
        {
            case ClipboardKeyAction.SelectAll:
                if (TextModel.Lines.All(l => l.Length == 0))
                {
                    return false;
                }

                SelectAll();
                return true;
            case ClipboardKeyAction.Copy when HasSelection:
                await ClipboardKeys.TrySetAsync(Clipboard, SelectedText);
                return true;
            case ClipboardKeyAction.Cut when HasSelection:
                await ClipboardKeys.TrySetAsync(Clipboard, SelectedText);
                if (!IsReadOnly)
                {
                    RemoveSelectedText();
                    IsDirty = true;
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

    /// <summary>
    /// With text selected, Backspace and Delete remove it, and typing or Enter replaces it (the key then goes on to
    /// type or split the line at the cursor the removal left). A read-only editor can select but not change.
    /// </summary>
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (IsEnabled && await HandleClipboardKeyAsync(keyEvent))
        {
            return true;
        }

        if (HasSelection && !IsReadOnly && IsEnabled && !keyEvent.Control && !keyEvent.Alt)
        {
            if (!keyEvent.Shift && keyEvent.ConsoleKey is ConsoleKey.Backspace or ConsoleKey.Delete)
            {
                RemoveSelectedText();
                IsDirty = true;
                return true;
            }

            if (ConsoleKeyHelper.IsTypingKey(keyEvent) || keyEvent.ConsoleKey == ConsoleKey.Enter)
            {
                RemoveSelectedText();
                IsDirty = true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Refresh()
    {
        if (TextModel.Lines.Count > 0)
        {
            ScrollableModel.ScrollableSize = new Data.Size(
                TextModel.Lines.Max(l => l.Length),
                TextModel.Lines.Count);
        }
        else
        {
            ScrollableModel.ScrollableSize = new Data.Size(0, 0);
        }
        KeepCursorInText();
        base.Refresh();
    }
}
